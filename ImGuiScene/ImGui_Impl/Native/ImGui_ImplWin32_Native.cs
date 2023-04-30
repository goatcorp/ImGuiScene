using System;
using System.Runtime.InteropServices;

namespace ImGuiScene.ImGui_Impl.Native {
    public unsafe class ImGui_ImplWin32_Native {
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool igImpls_ImplWin32_Init(void* hwnd);
        
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr igImpls_ImplWin32_WndProcHandler(void* hWnd, uint msg, void* wParam, void* lParam);
        
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern void igImpls_ImplWin32_Shutdown();
        
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern void igImpls_ImplWin32_NewFrame();
        
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern void igImpls_ImplWin32_EnableDpiAwareness();
        
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern float igImpls_ImplWin32_GetDpiScaleForHwnd(void* hwnd);
        
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern float igImpls_ImplWin32_GetDpiScaleForMonitor(void* monitor);
        
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern void igImpls_ImplWin32_EnableAlphaCompositing(void* hwnd);
    }
}
