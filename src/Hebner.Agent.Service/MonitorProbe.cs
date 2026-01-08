using Hebner.Agent.Shared;
using System.Windows.Forms;

namespace Hebner.Agent.Service;

// NOTE: Simple monitor probe stub. Replace with Windows.Graphics.Capture metadata later.
public static class MonitorProbe
{
    public static List<MonitorInfo> GetMonitorsSafe()
    {
        try
        {
            var list = new List<MonitorInfo>();
            var screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var s = screens[i];
                list.Add(new MonitorInfo(
                    MonitorId: $"screen-{i}",
                    Name: s.DeviceName,
                    Width: s.Bounds.Width,
                    Height: s.Bounds.Height,
                    Scale: 1.0,
                    IsPrimary: s.Primary,
                    SortOrder: i
                ));
            }
            return list;
        }
        catch
        {
            return new List<MonitorInfo> {
                new MonitorInfo("screen-0","Primary",1920,1080,1.0,true,0)
            };
        }
    }
}
