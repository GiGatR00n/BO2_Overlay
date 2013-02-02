using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using SlimDX.Direct2D;
using SlimDX.Direct3D11;
using SlimDX.DXGI;
using SlimDX;
using System.Runtime.InteropServices;
using EasyHook;
using System.Threading;
using System.IO;
using SlimDX.DirectWrite;
using SpriteTextRenderer;
using Device = SlimDX.Direct3D11.Device;

//using FactoryType = SlimDX.DirectWrite.FactoryType;

namespace ScreenshotInject
{

    enum D3D11DeviceVTbl : short
    {
        // IUnknown
        QueryInterface = 0,
        AddRef = 1,
        Release = 2,

        // ID3D11Device
        CreateBuffer = 3,
        CreateTexture1D = 4,
        CreateTexture2D = 5,
        CreateTexture3D = 6,
        CreateShaderResourceView = 7,
        CreateUnorderedAccessView = 8,
        CreateRenderTargetView = 9,
        CreateDepthStencilView = 10,
        CreateInputLayout = 11,
        CreateVertexShader = 12,
        CreateGeometryShader = 13,
        CreateGeometryShaderWithStreamOutput = 14,
        CreatePixelShader = 15,
        CreateHullShader = 16,
        CreateDomainShader = 17,
        CreateComputeShader = 18,
        CreateClassLinkage = 19,
        CreateBlendState = 20,
        CreateDepthStencilState = 21,
        CreateRasterizerState = 22,
        CreateSamplerState = 23,
        CreateQuery = 24,
        CreatePredicate = 25,
        CreateCounter = 26,
        CreateDeferredContext = 27,
        OpenSharedResource = 28,
        CheckFormatSupport = 29,
        CheckMultisampleQualityLevels = 30,
        CheckCounterInfo = 31,
        CheckCounter = 32,
        CheckFeatureSupport = 33,
        GetPrivateData = 34,
        SetPrivateData = 35,
        SetPrivateDataInterface = 36,
        GetFeatureLevel = 37,
        GetCreationFlags = 38,
        GetDeviceRemovedReason = 39,
        GetImmediateContext = 40,
        SetExceptionMode = 41,
        GetExceptionMode = 42,
    }

    /// <summary>
    /// Direct3D 11 Hook - this hooks the SwapChain.Present to take screenshots
    /// </summary>
    internal class DXHookD3D11: BaseDXHook
    {
        const int D3D11_DEVICE_METHOD_COUNT = 43;

        public DXHookD3D11(ScreenshotInterface.ScreenshotInterface ssInterface)
            : base(ssInterface)
        {
        }

        List<IntPtr> _d3d11VTblAddresses = null;
        List<IntPtr> _dxgiSwapChainVTblAddresses = null;

        LocalHook DXGISwapChain_PresentHook = null;
        LocalHook DXGISwapChain_ResizeTargetHook = null;
        LocalHook DXGISwapChain_D3D11CreateDevice = null;

        protected override string HookName
        {
            get
            {
                return "DXHookD3D11";
            }
        }

        public override void Hook()
        {
            this.DebugMessage("Hook: Begin");
            if (_d3d11VTblAddresses == null)
            {
                _d3d11VTblAddresses = new List<IntPtr>();
                _dxgiSwapChainVTblAddresses = new List<IntPtr>();

                #region Get Device and SwapChain method addresses
                // Create temporary device + swapchain and determine method addresses
                SlimDX.Direct3D11.Device device;
                SwapChain swapChain;
                using (SlimDX.Windows.RenderForm renderForm = new SlimDX.Windows.RenderForm())
                {
                    this.DebugMessage("Hook: Before device creation");
                    SlimDX.Result result = SlimDX.Direct3D11.Device.CreateWithSwapChain(
                        DriverType.Hardware,
                        DeviceCreationFlags.BgraSupport,
                        DXGI.CreateSwapChainDescription(renderForm.Handle),
                        out device,
                        out swapChain);

                    if (result.IsSuccess)
                    {
                        this.DebugMessage("Hook: Device created");
                        using (device)
                        {
                            _d3d11VTblAddresses.AddRange(GetVTblAddresses(device.ComPointer, D3D11_DEVICE_METHOD_COUNT));

                            using (swapChain)
                            {
                                _dxgiSwapChainVTblAddresses.AddRange(GetVTblAddresses(swapChain.ComPointer, DXGI.DXGI_SWAPCHAIN_METHOD_COUNT));
                            }
                        }
                    }
                    else
                    {
                        this.DebugMessage("Hook: Device creation failed");
                    }
                }
                #endregion
            }

            // We will capture the backbuffer here
            DXGISwapChain_PresentHook = LocalHook.Create(
                _dxgiSwapChainVTblAddresses[(int)DXGI.DXGISwapChainVTbl.Present],
                new DXGISwapChain_PresentDelegate(PresentHook),
                this);
            
            // We will capture target/window resizes here
            DXGISwapChain_ResizeTargetHook = LocalHook.Create(
                _dxgiSwapChainVTblAddresses[(int)DXGI.DXGISwapChainVTbl.ResizeTarget],
                new DXGISwapChain_ResizeTargetDelegate(ResizeTargetHook),
                this);

            DXGISwapChain_D3D11CreateDevice = LocalHook.Create(
                _dxgiSwapChainVTblAddresses[(int) DXGI.DXGISwapChainVTbl.D3D11CreateDevice],
                new DXGISwapChain_D3D11CreateDeviceDelegate(D3D11CreateDevice),
                this);

            /*
             * Don't forget that all hooks will start deactivated...
             * The following ensures that all threads are intercepted:
             * Note: you must do this for each hook.
             */
            DXGISwapChain_PresentHook.ThreadACL.SetExclusiveACL(new Int32[1]);

            DXGISwapChain_ResizeTargetHook.ThreadACL.SetExclusiveACL(new Int32[1]);

            DXGISwapChain_D3D11CreateDevice.ThreadACL.SetExclusiveACL(new Int32[1]);
        }

        public override void Cleanup()
        {
            try
            {
                if (DXGISwapChain_PresentHook != null)
                {
                    DXGISwapChain_PresentHook.Dispose();
                    DXGISwapChain_PresentHook = null;
                }
                if (DXGISwapChain_ResizeTargetHook != null)
                {
                    DXGISwapChain_ResizeTargetHook.Dispose();
                    DXGISwapChain_ResizeTargetHook = null;
                }
                if (DXGISwapChain_D3D11CreateDevice != null)
                {
                    DXGISwapChain_D3D11CreateDevice.Dispose();
                    DXGISwapChain_D3D11CreateDevice = null;
                }
                
                this.Request = null;
            }
            catch
            {
            }
        }

        /// <summary>
        /// The IDXGISwapChain.Present function definition
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns>
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int DXGISwapChain_PresentDelegate(IntPtr swapChainPtr, int syncInterval, /* int */ SlimDX.DXGI.PresentFlags flags);

        /// <summary>
        /// The IDXGISwapChain.ResizeTarget function definition
        /// </summary>
        /// <param name="device"></param>
        /// <param name="swapChainPtr"></param>
        /// <returns></returns>
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int DXGISwapChain_ResizeTargetDelegate(IntPtr swapChainPtr, ref DXGI.DXGI_MODE_DESC newTargetParameters);

        /// <summary>
        /// Testing IDXGISwapChain.CreateDeviceDelegate
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        private delegate int DXGISwapChain_D3D11CreateDeviceDelegate(
            Adapter pAdapter,
            DriverType driverType,
            long software,
            int flags,
            SlimDX.Direct3D11.FeatureLevel pFeatureLevel,
            int featureLevels,
            int sdkVersion,
            Device deviceOut,
            SlimDX.Direct3D11.FeatureLevel pFeatureLevelOut,
            DeviceContext ppImmediateContextOut);

        /// <summary>
        /// Hooked to allow resizing a texture/surface that is reused. Currently not in use as we create the texture for each request
        /// to support different sizes each time (as we use DirectX to copy only the region we are after rather than the entire backbuffer)
        /// </summary>
        /// <param name="swapChainPtr"></param>
        /// <param name="newTargetParameters"></param>
        /// <returns></returns>
        int ResizeTargetHook(IntPtr swapChainPtr, ref DXGI.DXGI_MODE_DESC newTargetParameters)
        {
			if (swapChainPtr != _swapChainPointer)
            {
                _swapChain = SlimDX.DXGI.SwapChain.FromPointer(swapChainPtr);
            }
            SwapChain swapChain = _swapChain;
            //using (SlimDX.DXGI.SwapChain swapChain = SlimDX.DXGI.SwapChain.FromPointer(swapChainPtr))
            {
                // This version creates a new texture for each request so there is nothing to resize.
                // IF the size of the texture is known each time, we could create it once, and then possibly need to resize it here

                return swapChain.ResizeTarget(
                    new SlimDX.DXGI.ModeDescription()
                    {
                        Format = newTargetParameters.Format,
                        Height = newTargetParameters.Height,
                        RefreshRate = newTargetParameters.RefreshRate,
                        Scaling = newTargetParameters.Scaling,
                        ScanlineOrdering = newTargetParameters.ScanlineOrdering,
                        Width = newTargetParameters.Width
                    }
                ).Code;
            }
        }

        DateTime? _lastFrame;
        private SwapChain _swapChain;
        private IntPtr _swapChainPointer;


        /// <summary>
        /// Our present hook that will grab a copy of the backbuffer when requested. Note: this supports multi-sampling (anti-aliasing)
        /// </summary>
        /// <param name="swapChainPtr"></param>
        /// <param name="syncInterval"></param>
        /// <param name="flags"></param>
        /// <returns>The HRESULT of the original method</returns>
        int PresentHook(IntPtr swapChainPtr, int syncInterval, SlimDX.DXGI.PresentFlags flags)
        {
            if (swapChainPtr != _swapChainPointer)
            {
                _swapChain = SlimDX.DXGI.SwapChain.FromPointer(swapChainPtr);
            }
            SwapChain swapChain = _swapChain;

            //using (SlimDX.DXGI.SwapChain swapChain = SlimDX.DXGI.SwapChain.FromPointer(swapChainPtr))
            {
                try
                {
                    #region Screenshot Request
                    if (this.Request != null)
                    {
                        this.DebugMessage("PresentHook: Request Start");
                        DateTime startTime = DateTime.Now;
                        using (Texture2D texture = Texture2D.FromSwapChain<Texture2D>(swapChain, 0))
                        {
                            #region Determine region to capture
                            System.Drawing.Rectangle regionToCapture = new System.Drawing.Rectangle(0, 0, texture.Description.Width, texture.Description.Height);

                            if (this.Request.RegionToCapture.Width > 0)
                            {
                                regionToCapture = this.Request.RegionToCapture;
                            }
                            #endregion

                            var theTexture = texture;

                            // If texture is multisampled, then we can use ResolveSubresource to copy it into a non-multisampled texture
                            Texture2D textureResolved = null;
                            if (texture.Description.SampleDescription.Count > 1)
                            {
                                this.DebugMessage("PresentHook: resolving multi-sampled texture");
                                // texture is multi-sampled, lets resolve it down to single sample
                                textureResolved = new Texture2D(texture.Device, new Texture2DDescription()
                                {
                                    CpuAccessFlags = CpuAccessFlags.None,
                                    Format = texture.Description.Format,
                                    Height = texture.Description.Height,
                                    Usage = ResourceUsage.Default,
                                    Width = texture.Description.Width,
                                    ArraySize = 1,
                                    SampleDescription = new SlimDX.DXGI.SampleDescription(1, 0), // Ensure single sample
                                    BindFlags = BindFlags.None,
                                    MipLevels = 1,
                                    OptionFlags = texture.Description.OptionFlags
                                });
                                // Resolve into textureResolved
                                texture.Device.ImmediateContext.ResolveSubresource(texture, 0, textureResolved, 0, texture.Description.Format);

                                // Make "theTexture" be the resolved texture
                                theTexture = textureResolved;
                            }

                            // Create destination texture
                            Texture2D textureDest = new Texture2D(texture.Device, new Texture2DDescription()
                            {
                                CpuAccessFlags = CpuAccessFlags.None,// CpuAccessFlags.Write | CpuAccessFlags.Read,
                                Format = SlimDX.DXGI.Format.R8G8B8A8_UNorm, // Supports BMP/PNG
                                Height = regionToCapture.Height,
                                Usage = ResourceUsage.Default,// ResourceUsage.Staging,
                                Width = regionToCapture.Width,
                                ArraySize = 1,//texture.Description.ArraySize,
                                SampleDescription = new SlimDX.DXGI.SampleDescription(1, 0),// texture.Description.SampleDescription,
                                BindFlags = BindFlags.None,
                                MipLevels = 1,//texture.Description.MipLevels,
                                OptionFlags = texture.Description.OptionFlags
                            });

                            // Copy the subresource region, we are dealing with a flat 2D texture with no MipMapping, so 0 is the subresource index
                            theTexture.Device.ImmediateContext.CopySubresourceRegion(theTexture, 0, new ResourceRegion()
                            {
                                Top = regionToCapture.Top,
                                Bottom = regionToCapture.Bottom,
                                Left = regionToCapture.Left,
                                Right = regionToCapture.Right,
                                Front = 0,
                                Back = 1 // Must be 1 or only black will be copied
                            }, textureDest, 0, 0, 0, 0);

                            // Note: it would be possible to capture multiple frames and process them in a background thread

                            // Copy to memory and send back to host process on a background thread so that we do not cause any delay in the rendering pipeline
                            Guid requestId = this.Request.RequestId; // this.Request gets set to null, so copy the RequestId for use in the thread
                            ThreadPool.QueueUserWorkItem(delegate
                            {
                                //FileStream fs = new FileStream(@"c:\temp\temp.bmp", FileMode.Create);
                                //Texture2D.ToStream(testSubResourceCopy, ImageFileFormat.Bmp, fs);

                                DateTime startCopyToSystemMemory = DateTime.Now;
                                using (MemoryStream ms = new MemoryStream())
                                {
                                    Texture2D.ToStream(textureDest.Device.ImmediateContext, textureDest, ImageFileFormat.Bmp, ms);
                                    ms.Position = 0;
                                    this.DebugMessage("PresentHook: Copy to System Memory time: " + (DateTime.Now - startCopyToSystemMemory).ToString());

                                    DateTime startSendResponse = DateTime.Now;
                                    SendResponse(ms, requestId);
                                    this.DebugMessage("PresentHook: Send response time: " + (DateTime.Now - startSendResponse).ToString());
                                }

                                // Free the textureDest as we no longer need it.
                                textureDest.Dispose();
                                textureDest = null;
                                this.DebugMessage("PresentHook: Full Capture time: " + (DateTime.Now - startTime).ToString());
                            });

                            // Prevent the request from being processed a second time
                            this.Request = null;

                            // Make sure we free up the resolved texture if it was created
                            if (textureResolved != null)
                            {
                                textureResolved.Dispose();
                                textureResolved = null;
                            }



                        }
                        this.DebugMessage("PresentHook: Copy BackBuffer time: " + (DateTime.Now - startTime).ToString());
                        this.DebugMessage("PresentHook: Request End");




                    }
                    #endregion



                }
                catch (Exception e)
                {
                    // If there is an error we do not want to crash the hooked application, so swallow the exception
                    this.DebugMessage("PresentHook: Exeception: " + e.GetType().FullName + ": " + e.Message);
                    //return unchecked((int)0x8000FFFF); //E_UNEXPECTED
                }

                if (this.ShowOverlay)
                {
                    using (Texture2D texture1 = Texture2D.FromSwapChain<SlimDX.Direct3D11.Texture2D>(swapChain, 0))
                    {

                        if (_lastFrame != null)
                        {
                            //                                    DrawOverlay(swapChain, texture1);
                            DrawBmp(swapChain, texture1);
                        }
                        _lastFrame = DateTime.Now;
                    }
                }


                // As always we need to call the original method, note that EasyHook has already repatched the original method
                // so calling it here will not cause an endless recursion to this function
                return swapChain.Present(syncInterval, flags).Code;
            }
        }


        void DrawBmp(SwapChain swapChain, Texture2D texture)
        {

            SpriteRenderer sprite = new SpriteRenderer(texture.Device);

//            var srv = new ShaderResourceView(texture.Device,
//                                             Texture2D.FromFile(texture.Device, "S:\\Downloads\font.jpg"));

//            sprite.Draw(srv, new Vector2(30, 30), new Vector2(50, 50), CoordinateType.Absolute);


            var myTextBlockRenderer = new TextBlockRenderer(sprite, "Arial", FontWeight.Bold, SlimDX.DirectWrite.FontStyle.Normal, FontStretch.Normal, 12);
            myTextBlockRenderer.DrawString("Example Text", Vector2.Zero, Color.White);

            sprite.Flush();
        }

        // Vertex Structure
        // LayoutKind.Sequential is required to ensure the public variables
        // are written to the datastream in the correct order.
        [StructLayout(LayoutKind.Sequential)]
        struct VertexPositionColor
        {

            public Vector4 Position;
            public Color4 Color;
            public static readonly InputElement[] inputElements = new[] {
                new InputElement("POSITION", 0, Format.R32G32B32A32_Float, 0, 0),
                new InputElement("COLOR",0,Format.R32G32B32A32_Float,16,0)
            };
            public static readonly int SizeInBytes = Marshal.SizeOf(typeof(VertexPositionColor));
            public VertexPositionColor(Vector4 position, Color4 color)
            {
                Position = position;
                Color = color;
            }
            public VertexPositionColor(Vector3 position, Color4 color)
            {
                Position = new Vector4(position, 1);
                Color = color;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct VertexPositionTexture
        {
            public Vector4 Position;
            public Vector2 TexCoord;
            public static readonly InputElement[] inputElements = new[] {
                new InputElement("POSITION", 0, Format.R32G32B32A32_Float, 0, 0),
                new InputElement("TEXCOORD",0,Format.R32G32_Float, 16 ,0)
            };
            public static readonly int SizeInBytes = Marshal.SizeOf(typeof(VertexPositionTexture));
            public VertexPositionTexture(Vector4 position, Vector2 texCoord)
            {
                Position = position;
                TexCoord = texCoord;
            }
            public VertexPositionTexture(Vector3 position, Vector2 texCoord)
            {
                Position = new Vector4(position, 1);
                TexCoord = texCoord;
            }
        }

//            __in_opt IDXGIAdapter* pAdapter,
//    D3D_DRIVER_TYPE DriverType,
//    HMODULE Software,
//    UINT Flags,
//    __in_ecount_opt( FeatureLevels ) CONST D3D_FEATURE_LEVEL* pFeatureLevels,
//    UINT FeatureLevels,
//    UINT SDKVersion,
//    __out_opt ID3D11Device** ppDevice,
//    __out_opt D3D_FEATURE_LEVEL* pFeatureLevel,
//    __out_opt ID3D11DeviceContext** ppImmediateContext
        int D3D11CreateDevice(
            Adapter pAdapter, 
            DriverType driverType,
            long software, 
            int flags, 
            SlimDX.Direct3D11.FeatureLevel pFeatureLevel,
            int featureLevels,
            int sdkVersion,
            Device deviceOut,
            SlimDX.Direct3D11.FeatureLevel pFeatureLevelOut,
            DeviceContext ppImmediateContextOut)
        {
            this.DebugMessage("Enter::D3D11CreateDevice");
            return 0;
        }
    }
}
