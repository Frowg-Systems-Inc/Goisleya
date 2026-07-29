using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var firstDeath = PressureCoachLogic.FirstDeath(alreadySeen: false);
Check(firstDeath.Show && firstDeath.CoachId == PressureCoachLogic.FirstDeathId,
    "first death coach shows once");
Check(firstDeath.Detail.Contains("Streamer Mode hides markers"),
    "first death coach sets streamer expectations");
var seenDeath = PressureCoachLogic.FirstDeath(alreadySeen: true);
Check(!seenDeath.Show, "seen death coach stays hidden");
Check(string.IsNullOrEmpty(seenDeath.CoachId)
      && string.IsNullOrEmpty(seenDeath.Title)
      && string.IsNullOrEmpty(seenDeath.Detail),
    "hidden coaches return a fully empty presentation");

var firstNest = PressureCoachLogic.FirstNest(alreadySeen: false, nestActive: true);
Check(firstNest.Show && firstNest.CoachId == PressureCoachLogic.FirstNestId,
    "first nest coach shows for an active nest");
Check(!PressureCoachLogic.FirstNest(alreadySeen: false, nestActive: false).Show,
    "nest coach requires an active nest");
Check(!PressureCoachLogic.FirstNest(alreadySeen: true, nestActive: true).Show,
    "seen nest coach stays hidden");

var roster = PressureCoachLogic.ConsentRoster(
    alreadySeen: false,
    liveNetworkConnected: true,
    consentFiltered: true,
    friendSharingOn: false,
    grantCount: 0,
    friendCount: 0);
Check(roster.Show && roster.CoachId == PressureCoachLogic.ConsentRosterId,
    "empty consent-filtered roster coaches");
Check(roster.Detail.Contains("Consent-filtered server"),
    "roster coach explains consent filtering");
Check(roster.Title == "NO FRIENDS VISIBLE YET", "roster coach keeps the honest title");

var waiting = PressureCoachLogic.ConsentRoster(false, true, true, true, 0, 0);
Check(waiting.Show && waiting.Detail.Contains("not a broken connection"),
    "sharing-on roster coach reassures instead of blaming");
Check(PressureCoachLogic.ConsentRoster(false, true, true, false, 2, 0).Detail
          .Contains("not a broken connection"),
    "pending grants also reassure");

Check(!PressureCoachLogic.ConsentRoster(true, true, true, false, 0, 0).Show,
    "seen roster coach stays hidden");
Check(!PressureCoachLogic.ConsentRoster(false, false, true, false, 0, 0).Show,
    "roster coach requires a live connection");
Check(!PressureCoachLogic.ConsentRoster(false, true, false, false, 0, 0).Show,
    "roster coach only applies to consent-filtered servers");
Check(!PressureCoachLogic.ConsentRoster(false, true, true, false, 0, 3).Show,
    "visible friends dismiss the roster coach");

var preStream = PressureCoachLogic.PreStream(alreadySeen: false);
Check(preStream.Show && preStream.CoachId == PressureCoachLogic.PreStreamId,
    "pre-stream coach shows once");
Check(preStream.Detail.Contains("Toggle again to restore"),
    "pre-stream coach explains reversibility");
Check(!PressureCoachLogic.PreStream(alreadySeen: true).Show, "seen pre-stream coach stays hidden");

var ids = new[]
{
    PressureCoachLogic.FirstDeathId,
    PressureCoachLogic.FirstNestId,
    PressureCoachLogic.ConsentRosterId,
    PressureCoachLogic.PreStreamId
};
Check(ids.Distinct().Count() == ids.Length, "coach identifiers are unique");

Console.WriteLine(
    "Pressure coach verification passed (once-only gating, consent-roster honesty, reassurance copy, streamer expectations, and unique coach IDs).");
