using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var steps = OnboardingTutorialLogic.Steps;
Check(steps.Count == 5,
    "The quick start must stay short at exactly five focused steps.");
Check(OnboardingTutorialLogic.CurrentVersion == 6,
    "The Player Sync, automatic location, and proximity-voice onboarding must supersede earlier tutorials.");
Check(OnboardingTutorialLogic.ShouldShow(0)
      && !OnboardingTutorialLogic.ShouldShow(OnboardingTutorialLogic.CurrentVersion),
    "The tutorial must appear for an unfinished version and stay dismissed once completed or skipped.");
Check(OnboardingTutorialLogic.NormalizeIndex(-10) == 0
      && OnboardingTutorialLogic.NormalizeIndex(99) == steps.Count - 1
      && OnboardingTutorialLogic.Move(0, -1) == 0
      && OnboardingTutorialLogic.Move(steps.Count - 1, 1) == steps.Count - 1,
    "Tutorial navigation must remain within the five-step tour.");
Check(OnboardingTutorialLogic.IsFirst(0)
      && OnboardingTutorialLogic.IsLast(steps.Count - 1)
      && OnboardingTutorialLogic.ProgressLabel(2) == "3 OF 5"
      && OnboardingTutorialLogic.NextLabel(steps.Count - 1) == "START MAPPING",
    "Tutorial progress and final action copy must be deterministic.");

var combinedCopy = string.Join(
    "\n",
    steps.Select(step => $"{step.Kicker}\n{step.Title}\n{step.Body}\n{step.Tip}"));
foreach (var requiredPhrase in new[]
         {
             "click-through",
             "RECENTER",
             "road-and-trail course",
             "vitals",
             "push-to-talk voice",
             "Quick Commands",
             "Lite Mode",
             "Streamer Mode",
             "never invents a position",
             "Verify cliffs",
             "Any Server",
             "private",
             "unlisted",
             "Terrain Probe",
             "bundled map",
             "Isley join link",
             "Steam",
             "server-wide"
         })
{
    Check(combinedCopy.Contains(requiredPhrase, StringComparison.OrdinalIgnoreCase),
        $"The quick start is missing required truthful guidance: {requiredPhrase}");
}

var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");
var source = string.Join("\n", Directory.GetFiles(Path.Combine(root, "BurntHud"), "MainWindow*.cs").OrderBy(p => p, StringComparer.Ordinal).Select(File.ReadAllText));
var xaml = File.ReadAllText(Path.Combine(root, "BurntHud", "MainWindow.xaml"));

Check(xaml.Contains("x:Name=\"OnboardingTutorialLayer\"", StringComparison.Ordinal)
      && xaml.Contains("x:Name=\"OnboardingProgressBar\"", StringComparison.Ordinal)
      && xaml.Contains("x:Name=\"OnboardingBackButton\"", StringComparison.Ordinal)
      && xaml.Contains("x:Name=\"OnboardingNextButton\"", StringComparison.Ordinal)
      && xaml.Contains("x:Name=\"OnboardingSkipButton\"", StringComparison.Ordinal)
      && xaml.Contains("KeyboardNavigation.TabNavigation=\"Cycle\"", StringComparison.Ordinal)
      && xaml.Contains("AutomationProperties.Name=\"Isley quick start tutorial\"", StringComparison.Ordinal),
    "The tutorial must expose a modal, progress, back, next, skip, focus cycle, and accessibility surface.");
Check(xaml.Contains("x:Name=\"OnboardingReplayButton\"", StringComparison.Ordinal)
      && source.Contains("OnboardingReplayButton_Click", StringComparison.Ordinal)
      && source.Contains("new(\"tutorial\", \"Replay Isley quick start\"", StringComparison.Ordinal),
    "The tutorial must remain replayable from App and Quick Commands.");
Check(xaml.Contains("x:Name=\"OnboardingServerChoicePanel\"", StringComparison.Ordinal)
      && xaml.Contains("x:Name=\"OnboardingServerLiveMapButton\"", StringComparison.Ordinal)
      && xaml.Contains("x:Name=\"OnboardingServerOfficialButton\"", StringComparison.Ordinal)
      && xaml.Contains("x:Name=\"OnboardingServerAnyButton\"", StringComparison.Ordinal)
      && source.Contains("ServerSessionProfileButton_Click", StringComparison.Ordinal)
      && source.Contains("OnboardingServerChoicePanel.Visibility", StringComparison.Ordinal),
    "The first tutorial step must provide explicit Live Map, Official, and Any Server selection.");
Check(xaml.Contains("AutomationProperties.Name=\"Onboarding independent map disclosure\"", StringComparison.Ordinal)
      && xaml.Contains(
          "Isley's map stays independent and uses an attributed public Gateway feed. A participating server may optionally provide an Isley join link for its own authorized continuous telemetry.",
          StringComparison.Ordinal),
    "The tutorial must show the independent-map disclosure and use Live Map branding.");
Check(source.Contains("public int OnboardingTutorialVersionCompleted", StringComparison.Ordinal)
      && source.Contains("_onboardingTutorialVersionCompleted = Math.Max", StringComparison.Ordinal)
      && source.Contains("OnboardingTutorialVersionCompleted = _onboardingTutorialVersionCompleted", StringComparison.Ordinal)
      && source.Contains("OnboardingTutorialLogic.ShouldShow(_onboardingTutorialVersionCompleted)", StringComparison.Ordinal),
    "Tutorial completion must round-trip through normal Isley settings.");
Check(source.Contains("CloseOnboardingTutorial(completed: false)", StringComparison.Ordinal)
      && source.Contains("OnboardingTutorialLogic.CurrentVersion", StringComparison.Ordinal)
      && source.Contains("key == Key.Escape", StringComparison.Ordinal)
      && source.Contains("key == Key.Left", StringComparison.Ordinal)
      && source.Contains("key == Key.Right", StringComparison.Ordinal),
    "Skip, close, and keyboard navigation must be implemented.");
Check(source.Contains("SetClickThrough(false)", StringComparison.Ordinal)
      && source.Contains("OnboardingNextButton.Focus()", StringComparison.Ordinal),
    "Opening the tutorial must restore interaction and place keyboard focus inside the tour.");

Console.WriteLine(
    "Onboarding tutorial verification passed (5 steps, live-network orientation, interactive server selection, universal capability boundaries, " +
    "first-run persistence, skip/back/next controls, keyboard navigation, and App/Quick Commands replay).");
