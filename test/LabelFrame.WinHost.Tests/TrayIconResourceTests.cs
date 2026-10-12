using System.Drawing;

namespace LabelFrame.WinHost.Tests;

/// <summary>
/// 托盘图标资源防线：内嵌白色变体存在且帧覆盖托盘槽尺寸（16~48，DPI 100%~200%）、
/// 加载失败返回 null（托盘回退应用图标不崩宿主）。
/// </summary>
public class TrayIconResourceTests
{
    [Fact]
    public void TryLoadTrayIcon_should_load_with_system_tray_slot_size()
    {
        using var icon = TrayIconService.TryLoadTrayIcon();
        Assert.NotNull(icon);
        // GDI+ 按托盘槽尺寸选最接近帧：选中帧尺寸非零且在托盘量级（不超 64，防意外选大帧）
        Assert.True(icon!.Size.Width > 0 && icon.Size.Width <= 64, $"帧宽度异常：{icon.Size.Width}");
        Assert.True(icon.Size.Height > 0 && icon.Size.Height <= 64, $"帧高度异常：{icon.Size.Height}");
    }

    [Fact]
    public void Embedded_resource_should_contain_all_tray_frame_sizes()
    {
        using var stream = typeof(TrayIconService).Assembly
            .GetManifestResourceStream(TrayIconService.TrayIconResourceName);
        Assert.NotNull(stream);

        using (stream!)
        {
            using var br = new BinaryReader(stream);
            Assert.Equal(0, br.ReadInt16());   // reserved
            Assert.Equal(1, br.ReadInt16());   // type = icon
            var frames = br.ReadInt16();
            var widths = new HashSet<int>();
            for (var i = 0; i < frames; i++)
            {
                var w = br.ReadByte();
                br.ReadBytes(15); // entry 其余字段（高/色深/保留 + 字节数/偏移）
                widths.Add(w == 0 ? 256 : w);
            }

            // 托盘槽尺寸（SM_CXSMICON 随 DPI：16/20/24/32）全部有原生帧，避免靠系统缩放发糊
            Assert.True(
                widths.IsSupersetOf(new HashSet<int> { 16, 20, 24, 32, 48 }),
                $"托盘帧覆盖不足，实际帧：{string.Join(", ", widths.OrderBy(x => x))}");
        }
    }
}
