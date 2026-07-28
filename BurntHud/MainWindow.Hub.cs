using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Net.WebSockets;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Isley.Telemetry;
using Microsoft.Web.WebView2.Core;

namespace Isley;

public partial class MainWindow
{
    private void OpenHubButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("life-run");

    private void OpenKofiButton_Click(object sender, RoutedEventArgs e) => OpenExternalUri(OverlayLinks.KoFi);

    private async void OpenLocationPrivacyButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowHotkeyToastAsync(
            _streamerMode
                ? "STREAMER MODE HIDES PROVIDER POSITIONS"
                : "USE STREAMER MODE TO HIDE PROVIDER POSITIONS",
            true);
    }

    private void OpenCurrentDinoButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("core-vitals");

    private void OpenGarageButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("life-run");

    private void OpenDinoShopButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("mutation-planner");

    private void OpenSkinShopButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("field-conditions");

    private void OpenSkinEditorButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("field-conditions");

    private void OpenTeleportsButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("routes");

    private void OpenQuestsButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("life-run");

    private void OpenBattlepassButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("growth-clock");

    private void OpenKillfeedButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("sighting-check");

    private void OpenLeaderboardButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("trip-check");

    private void OpenCasinoButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("fight-check");

    private void OpenCasesButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("resource-finder");

    private void OpenBetsButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("next-move");

    private void OpenPlayerGuideButton_Click(object sender, RoutedEventArgs e) =>
        OpenMapToolsAtSection("life-run");

    private void OpenPrimeGuideButton_Click(object sender, RoutedEventArgs e) => OpenExternalUri(OverlayLinks.PrimeGuide);

    private void OpenMutationGuideButton_Click(object sender, RoutedEventArgs e) => OpenExternalUri(OverlayLinks.MutationGuide);

    private void OpenDietGuideButton_Click(object sender, RoutedEventArgs e) => OpenExternalUri(OverlayLinks.DietGuide);

    private void OpenCombatGuideButton_Click(object sender, RoutedEventArgs e) => OpenExternalUri(OverlayLinks.CombatGuide);

    private void OpenSurvivalGuideButton_Click(object sender, RoutedEventArgs e) => OpenExternalUri(OverlayLinks.CombatGuide);

    private void OpenControlsGuideButton_Click(object sender, RoutedEventArgs e) => OpenExternalUri(OverlayLinks.ControlsGuide);

    private void OpenGrowthGuideButton_Click(object sender, RoutedEventArgs e) => OpenExternalUri(OverlayLinks.GrowthGuide);

    private void OpenNestingGuideButton_Click(object sender, RoutedEventArgs e) => OpenExternalUri(OverlayLinks.NestingGuide);

    private void OpenZonesGuideButton_Click(object sender, RoutedEventArgs e) => OpenExternalUri(OverlayLinks.ZonesGuide);
}
