using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HandheldCompanion.Views.Classes
{
    public class MenuColorTable : ProfessionalColorTable
    {
        public MenuColorTable()
        {
            UseSystemColors = false;
        }
        public override Color ToolStripDropDownBackground => Color.DarkGoldenrod;
        public override Color MenuBorder
        {
            get { return Color.Fuchsia; }
        }
        public override Color MenuItemBorder
        {
            get { return Color.DarkViolet; }
        }
        public override Color MenuItemSelected
        {
            get { return Color.Cornsilk; }
        }
        public override Color MenuItemSelectedGradientBegin
        {
            get { return Color.LawnGreen; }
        }
        public override Color MenuItemSelectedGradientEnd
        {
            get { return Color.MediumSeaGreen; }
        }
        public override Color MenuStripGradientBegin
        {
            get { return Color.AliceBlue; }
        }
        public override Color MenuStripGradientEnd
        {
            get { return Color.DodgerBlue; }
        }
        public override Color ImageMarginGradientBegin
        {
            get { return Color.LawnGreen; }
        }
        public override Color ImageMarginGradientEnd
        {
            get { return Color.MediumSeaGreen; }
        }
        public override Color ImageMarginGradientMiddle => Color.MediumSeaGreen;
    }

    public class CustomContextMenu : ContextMenuStrip
    {
        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern long DwmSetWindowAttribute(nint hwnd,
                                                            DWMWINDOWATTRIBUTE attribute,
                                                            ref DWM_WINDOW_CORNER_PREFERENCE pvAttribute,
                                                            uint cbAttribute);


        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int SetPreferredAppMode(int preferredAppMode);

        [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern void FlushMenuThemes();

        public CustomContextMenu()
        {
            var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;     //change as you want
            DwmSetWindowAttribute(Handle,
                                  DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE,
                                  ref preference,
                                  sizeof(uint));
            RenderMode = ToolStripRenderMode.Professional;
            Renderer = new ToolStripProfessionalRenderer(new MenuColorTable());
            SetPreferredAppMode(2);
            FlushMenuThemes();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
        }

        public enum DWMWINDOWATTRIBUTE
        {
            DWMWA_WINDOW_CORNER_PREFERENCE = 33
        }
        public enum DWM_WINDOW_CORNER_PREFERENCE
        {
            DWMWA_DEFAULT = 0,
            DWMWCP_DONOTROUND = 1,
            DWMWCP_ROUND = 2,
            DWMWCP_ROUNDSMALL = 3,
        }

    }
}
