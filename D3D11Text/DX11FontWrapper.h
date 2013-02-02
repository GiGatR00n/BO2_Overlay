#pragma once
#include "FW1FontWrapper.h"
using namespace System::Runtime::InteropServices;

public ref class DX11FontWrapper
{
public:
DX11FontWrapper(SlimDX::Direct3D11::Device^ device);
void Draw(System::String^ str,float size,int x,int y,int color);
private:
SlimDX::Direct3D11::Device^ device;
IFW1FontWrapper* pFontWrapper;
};