using System.Drawing;
using System.Windows.Forms;

namespace TempBar
{
    /// <summary>Dark tool-strip colours so the popup menu matches the Windows 11 taskbar.</summary>
    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        private static readonly Color Bg = Color.FromArgb(43, 43, 43);
        private static readonly Color BgHover = Color.FromArgb(64, 64, 64);
        private static readonly Color Border = Color.FromArgb(70, 70, 70);

        public override Color ToolStripDropDownBackground { get { return Bg; } }
        public override Color ImageMarginGradientBegin { get { return Bg; } }
        public override Color ImageMarginGradientMiddle { get { return Bg; } }
        public override Color ImageMarginGradientEnd { get { return Bg; } }
        public override Color MenuBorder { get { return Border; } }
        public override Color MenuItemBorder { get { return BgHover; } }
        public override Color MenuItemSelected { get { return BgHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return BgHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return BgHover; } }
        public override Color MenuItemPressedGradientBegin { get { return Bg; } }
        public override Color MenuItemPressedGradientEnd { get { return Bg; } }
        public override Color SeparatorDark { get { return Border; } }
        public override Color SeparatorLight { get { return Bg; } }
        public override Color CheckBackground { get { return BgHover; } }
        public override Color CheckSelectedBackground { get { return BgHover; } }
        public override Color CheckPressedBackground { get { return BgHover; } }
        public override Color ButtonSelectedHighlight { get { return BgHover; } }
        public override Color ButtonSelectedBorder { get { return Border; } }
        public override Color ButtonSelectedGradientBegin { get { return BgHover; } }
        public override Color ButtonSelectedGradientEnd { get { return BgHover; } }
        public override Color ButtonPressedGradientBegin { get { return BgHover; } }
        public override Color ButtonPressedGradientEnd { get { return BgHover; } }
        public override Color StatusStripGradientBegin { get { return Bg; } }
        public override Color StatusStripGradientEnd { get { return Bg; } }
    }

    internal sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (!e.Item.Enabled) e.TextColor = Color.FromArgb(120, 120, 120);
            else if (e.Item.Selected) e.TextColor = Color.White;
            else e.TextColor = Color.FromArgb(235, 235, 235);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Selected ? Color.White : Color.FromArgb(200, 200, 200);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var brush = new SolidBrush(Color.FromArgb(43, 43, 43)))
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Color.FromArgb(70, 70, 70)))
            {
                Rectangle r = e.AffectedBounds;
                e.Graphics.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
            }
        }
    }
}
