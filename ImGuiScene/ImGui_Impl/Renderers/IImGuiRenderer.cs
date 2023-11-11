using System;
using ImGuiNET;

namespace ImGuiScene
{
    /// <summary>
    /// A simple shared public interface that all ImGui render implementations follow.
    /// </summary>
    public interface IImGuiRenderer
    {
        public delegate void DrawCmdUserCallbackDelegate(ImDrawDataPtr drawData, ImDrawCmdPtr drawCmd);

        public nint ResetDrawCmdUserCallback { get; }

        // FIXME - probably a better way to do this than params object[] !
        void Init(params object[] initParams);
        void Shutdown();
        void NewFrame();
        void RenderDrawData(ImGuiNET.ImDrawDataPtr drawData);
        public nint AddDrawCmdUserCallback(DrawCmdUserCallbackDelegate @delegate);
        public void RemoveDrawCmdUserCallback(DrawCmdUserCallbackDelegate @delegate);
    }
}
