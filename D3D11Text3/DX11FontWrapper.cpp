#include "StdAfx.h"
#include "DX11FontWrapper.h"


DX11FontWrapper::DX11FontWrapper(SlimDX::Direct3D11::Device^ device)
{
this->device = device;

IFW1Factory *pFW1Factory;
FW1CreateFactory(FW1_VERSION, &pFW1Factory);
ID3D11Device* dev = (ID3D11Device*)device->ComPointer.ToPointer();



IFW1FontWrapper* pw;

pFW1Factory->CreateFontWrapper(dev, L"Arial", &pw); 
pFW1Factory->Release();

this->pFontWrapper = pw;
}

void DX11FontWrapper::Draw(System::String^ str,float size,int x,int y, int color)
{
ID3D11DeviceContext* pImmediateContext = (ID3D11DeviceContext*)this->device->ImmediateContext->ComPointer.ToPointer();
void* txt = (void*)Marshal::StringToHGlobalUni(str);
pFontWrapper->DrawString(pImmediateContext, (WCHAR*)txt, size, x, y, color, 0);
    Marshal::FreeHGlobal(System::IntPtr(txt));
}