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
        }catch(Exception error){failure=error;}});
        worker.SetApartmentState(ApartmentState.STA);worker.Start();if(!worker.Join(TimeSpan.FromSeconds(10)))throw new Exception("scroll layout check timed out");if(failure!=null)throw failure;
    }
}
