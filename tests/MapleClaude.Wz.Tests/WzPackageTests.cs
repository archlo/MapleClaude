using FluentAssertions;
using Xunit;

namespace MapleClaude.Wz.Tests;

/// <summary>
/// Real-asset tests for the WZ reader. Each test silently returns (showing as
/// "passed") when <c>MAPLECLAUDE_WZ_DIR</c> isn't set, so CI builds without
/// the assets don't fail. Local dev runs that have the env var pointing at a
/// v95 GMS install exercise the full open + navigate + decode path.
/// </summary>
public class WzPackageTests
{
    private static string? WzDir =>
        Environment.GetEnvironmentVariable("MAPLECLAUDE_WZ_DIR");

    private static bool TryOpenUi(out WzPackage? pkg)
    {
        pkg = null;
        if (string.IsNullOrWhiteSpace(WzDir))
        {
            return false;
        }
        var path = Path.Combine(WzDir, "UI.wz");
        if (!File.Exists(path))
        {
            return false;
        }
        pkg = WzPackage.Open(path);
        return true;
    }

    [Fact]
    public void Open_UI_wz_and_walk_to_Login_Common_BtStart()
    {
        if (!TryOpenUi(out var ui))
        {
            return;
        }
        using (ui)
        {
            // v95 GMS UI.wz has BtStart under Login.img/Common
            // (older v83 had a /version vector here; not present in this dump).
            var btStart = ui!.GetItem("Login.img/Common/BtStart");
            btStart.Should().NotBeNull("Login.img/Common/BtStart exists in v95 UI.wz");
        }
    }

    [Fact]
    public void Open_UI_wz_and_load_login_button_canvas()
    {
        if (!TryOpenUi(out var ui))
        {
            return;
        }
        using (ui)
        {
            var loginButton = ui!.GetItem("Login.img/Title/BtLogin/normal/0") as WzCanvas
                              ?? ui.GetItem("Login.img/Title_new/BtLogin/normal/0") as WzCanvas;
            loginButton.Should().NotBeNull("Login button normal/0 sprite exists in either Title or Title_new");
            loginButton!.Width.Should().BeGreaterThan(0);
            loginButton.Height.Should().BeGreaterThan(0);
            loginButton.Format.Should().BeOneOf(1, 2, 513, 257, 517, 1026, 2050);
        }
    }

    [Fact]
    public void Decoded_login_button_pixels_have_expected_byte_count()
    {
        if (!TryOpenUi(out var ui))
        {
            return;
        }
        using (ui)
        {
            var loginButton = ui!.GetItem("Login.img/Title/BtLogin/normal/0") as WzCanvas
                              ?? ui.GetItem("Login.img/Title_new/BtLogin/normal/0") as WzCanvas;
            loginButton.Should().NotBeNull();
            var pixels = loginButton!.DecodeBgra();
            pixels.Length.Should().Be(loginButton.Width * loginButton.Height * 4,
                "BGRA32 = 4 bytes per pixel");
        }
    }
}
