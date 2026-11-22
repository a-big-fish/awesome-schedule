using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AwesomeInterview.NativeDemo;

static class ScrollPresentationChecks
{
    public static void Run(Action<bool,string> check)
    {
        Exception? failure=null;
        var worker=new Thread(()=>{try{
            var viewer=new QuietScrollViewer{Width=320,Height=200,Content=new Border{Width=800,Height=1200},VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};
            viewer.ApplyTemplate();viewer.Measure(new Size(320,200));viewer.Arrange(new Rect(0,0,320,200));viewer.UpdateLayout();
            var vertical=(ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar",viewer);
            var horizontal=(ScrollBar)viewer.Template.FindName("PART_HorizontalScrollBar",viewer);
            check(vertical.Opacity==0&&horizontal.Opacity==0&&!vertical.IsHitTestVisible,"overlay scroll rails start hidden and do not intercept input");
            check(viewer.ViewportWidth>=319&&viewer.ViewportHeight>=199,"hidden scroll rails reserve no content gutter");
            viewer.ScrollToVerticalOffset(120);viewer.ScrollToHorizontalOffset(80);viewer.UpdateLayout();
            check(viewer.VerticalOffset==120&&viewer.HorizontalOffset==80,"quiet scroll template preserves both scrolling axes");
            bool firstNotch = viewer.TryScrollWithWheel(-120);
            bool secondNotch = viewer.TryScrollWithWheel(-120);
            viewer.UpdateLayout();
            check(firstNotch && secondNotch && viewer.VerticalOffset == 216,
                "consecutive redirected wheel input accumulates before layout");
            check(vertical.Opacity == .5, "redirected wheel reveals the otherwise hidden rail");
            bool reachedEnd = viewer.TryScrollWithWheel(-3000);
            bool atEnd = viewer.TryScrollWithWheel(-120);
            viewer.UpdateLayout();
            check(reachedEnd && !atEnd && viewer.VerticalOffset == viewer.ScrollableHeight,
                "redirected wheel clamps to the bottom and declines further input for outer scrolling");
            viewer.ScrollToTop();
            viewer.UpdateLayout();
            check(!viewer.TryScrollWithWheel(120) && viewer.TryScrollWithWheel(-60),
                "redirected wheel respects the top boundary and accepts high-resolution input");
            viewer.UpdateLayout();
            check(viewer.VerticalOffset == 24, "a partial wheel notch moves proportionally");
        }catch(Exception error){failure=error;}});
        worker.SetApartmentState(ApartmentState.STA);worker.Start();if(!worker.Join(TimeSpan.FromSeconds(10)))throw new Exception("scroll layout check timed out");if(failure!=null)throw failure;
    }
}
