using System.Globalization;
using System.Text.Json;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Controls.Shapes;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using System.Collections.Generic;
using System.Linq;
namespace Voltwise;

public partial class MainPage : ContentPage
{
private readonly HashSet<string> _selectedBackupApplianceIds = new();
    private const string SetupKey = "voltwise_household_setup";
    private const string AppliancesKey = "voltwise_appliances";
    private const string OutagesKey = "voltwise_outages";
    private const string BackupKey = "voltwise_backup";
    private const string ThemeKey = "voltwise_theme";
    private const string MonthlySnapshotsKey = "voltwise_monthly_snapshots";
    private const string BudgetKey = "VoltwiseMonthlyBudget";
    private const string BudgetAlertsKey = "VoltwiseBudgetAlerts";
    private const string BackupAlertsKey = "VoltwiseBackupAlerts";
    private const int DailyReminderNotificationId = 4001;
private List<MonthlySnapshot> _monthlySnapshots = new();

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
    
    private void SaveBudget()
{
    Preferences.Set(BudgetKey, _monthlyBudget);
}

    private string _selectedTab = "Home";
    private bool _isAnimating;
    private bool _isModalOpen;
    private bool _isSaving;
    private double _monthlyBudget;

    private string _modalType = "";
    private string? _editingApplianceId;

    private readonly Dictionary<string, Entry> _modalEntries = new();

    private DatePicker? _outageDatePicker;
    private TimePicker? _outageTimePicker;

    private HouseholdSetup? _household;
    private List<ApplianceRecord> _appliances = new();
    private List<OutageRecord> _outages = new();
    private BackupConfiguration? _backup;

    public MainPage()
{
InitializeComponent();

LoadData();
RefreshBackupPlanner();
InitializeModalSystem();
RefreshDashboard();
UpdateNavigation("Home");


_ = ScheduleDailyReminderAsync();

}

   
    private T? LoadJson<T>(string key)
    {
        try
        {
            string json = Preferences.Get(key, string.Empty);

            if (string.IsNullOrWhiteSpace(json))
                return default;

            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private void SaveJson<T>(string key, T value)
    {
        string json = JsonSerializer.Serialize(value, _jsonOptions);
        Preferences.Set(key, json);
    }

    private void LoadData()
    {
        _household = LoadJson<HouseholdSetup>(SetupKey);

        _appliances =
            LoadJson<List<ApplianceRecord>>(AppliancesKey)
            ?? new List<ApplianceRecord>();

        _outages =
            LoadJson<List<OutageRecord>>(OutagesKey)
            ?? new List<OutageRecord>();

        _backup = LoadJson<BackupConfiguration>(BackupKey);
        
        _monthlySnapshots =
    LoadJson<List<MonthlySnapshot>>(MonthlySnapshotsKey)
    ?? new List<MonthlySnapshot>();
    
    _monthlyBudget = Preferences.Get(BudgetKey, 0.0);
    }
    

    private void SaveHousehold()
    {
        if (_household == null)
            Preferences.Remove(SetupKey);
        else
            SaveJson(SetupKey, _household);
    }

    private void SaveAppliances()
    {
        SaveJson(AppliancesKey, _appliances);
    }

    private void SaveOutages()
    {
        SaveJson(OutagesKey, _outages);
    }

    private void SaveBackup()
    {
        if (_backup == null)
            Preferences.Remove(BackupKey);
        else
            SaveJson(BackupKey, _backup);
    }
    
    private void SaveMonthlySnapshots()
{
    SaveJson(MonthlySnapshotsKey, _monthlySnapshots);
}
private void RefreshBackupApplianceChoices()
{
BackupApplianceChoices.Children.Clear();

if (_appliances.Count == 0)
{
    BackupApplianceChoices.Children.Add(
        new Label
        {
            Text = "No appliances saved yet. Add appliances in Usage first.",
            TextColor = Colors.Gray,
            FontSize = 13
        });

    return;
}

foreach (var appliance in _appliances)
{
    var checkBox = new CheckBox
    {
        Color = Color.FromArgb("#A3E635"),
        VerticalOptions = LayoutOptions.Center
    };

    checkBox.IsChecked =
        _selectedBackupApplianceIds.Contains(appliance.Id);

    checkBox.CheckedChanged += (_, args) =>
    {
        if (args.Value)
        {
            _selectedBackupApplianceIds.Add(appliance.Id);
        }
        else
        {
            _selectedBackupApplianceIds.Remove(appliance.Id);
        }
    };

    var details = new VerticalStackLayout
    {
        Spacing = 3,
        VerticalOptions = LayoutOptions.Center,
        Children =
        {
            new Label
            {
                Text = appliance.Name,
                TextColor = Colors.White,
                FontAttributes = FontAttributes.Bold,
                FontSize = 14
            },
            new Label
            {
                Text =
                    $"{appliance.Watts * Math.Max(1, appliance.Quantity):N0} W total",
                TextColor = Colors.Gray,
                FontSize = 12
            }
        }
    };

    var row = new Grid
    {
        ColumnDefinitions =
        {
            new ColumnDefinition { Width = 42 },
            new ColumnDefinition { Width = GridLength.Star }
        },
        Padding = new Thickness(8, 5)
    };

    row.Add(checkBox, 0, 0);
    row.Add(details, 1, 0);

    BackupApplianceChoices.Children.Add(row);
}

}

private async void OnCalculateBackupPlanClicked(
object sender,
EventArgs e)
{
if (_backup == null)
{
await DisplayAlert(
"Backup system required",
"Configure your battery and inverter before calculating the backup plan.",
"OK");

    return;
}

if (!double.TryParse(
        BackupPlanHoursEntry.Text,
        out double requiredHours) ||
    !double.IsFinite(requiredHours) ||
    requiredHours <= 0 ||
    requiredHours > 168)
{
    await DisplayAlert(
        "Invalid duration",
        "Enter a duration greater than zero and no more than 168 hours.",
        "OK");

    return;
}

var selectedAppliances = _appliances
    .Where(a => _selectedBackupApplianceIds.Contains(a.Id))
    .ToList();

if (selectedAppliances.Count == 0)
{
    await DisplayAlert(
        "No appliances selected",
        "Select at least one appliance to calculate its backup requirements.",
        "OK");

    return;
}

double selectedLoadWatts = selectedAppliances.Sum(
    a => a.Watts * Math.Max(1, a.Quantity));

if (!double.IsFinite(selectedLoadWatts) ||
    selectedLoadWatts <= 0)
{
    await DisplayAlert(
        "Invalid appliance load",
        "Check the wattage and quantity of your selected appliances.",
        "OK");

    return;
}

double deliveredEnergyWh = BackupDeliveredEnergyWh();

if (!double.IsFinite(deliveredEnergyWh) ||
    deliveredEnergyWh <= 0)
{
    await DisplayAlert(
        "Invalid backup configuration",
        "Check your battery capacity, battery count, usable capacity and inverter efficiency.",
        "OK");

    return;
}

double estimatedRuntimeHours =
    deliveredEnergyWh / selectedLoadWatts;

double requiredEnergyWh =
    selectedLoadWatts * requiredHours;

double inverterRating = _backup.InverterRatedWatts;

bool exceedsInverter =
    inverterRating > 0 &&
    selectedLoadWatts > inverterRating;

bool meetsDuration =
    estimatedRuntimeHours >= requiredHours;

BackupPlanLoadLabel.Text =
    $"Selected load: {selectedLoadWatts:N0} W";

BackupPlanRuntimeLabel.Text =
    $"Estimated runtime: {FormatRuntime(estimatedRuntimeHours)}";

BackupPlanEnergyLabel.Text =
    $"Energy required: {requiredEnergyWh:N0} Wh";

if (exceedsInverter)
{
    BackupPlanStatusLabel.Text =
        $"WARNING: The selected load exceeds the inverter's " +
        $"rated output by {selectedLoadWatts - inverterRating:N0} W. " +
        "Reduce the load before using this plan.";
}
else if (meetsDuration)
{
    BackupPlanStatusLabel.Text =
        $"Estimated capacity is sufficient for {requiredHours:N1} hours. " +
        $"Estimated energy remaining after this period: " +
        $"{Math.Max(0, deliveredEnergyWh - requiredEnergyWh):N0} Wh. " +
        "Allow for startup surges and real-world battery losses.";
}
else
{
    BackupPlanStatusLabel.Text =
        $"The selected appliances may run for approximately " +
        $"{FormatRuntime(estimatedRuntimeHours)}, which is shorter " +
        $"than your {requiredHours:N1}-hour target. Reduce the load " +
        "or increase available battery capacity.";
}

}

private void RefreshBackupPlanner()
{
RefreshBackupApplianceChoices();

BackupPlanLoadLabel.Text =
    "Selected load: not calculated";

BackupPlanRuntimeLabel.Text =
    "Estimated runtime: not calculated";

BackupPlanEnergyLabel.Text =
    "Energy required: not calculated";

BackupPlanStatusLabel.Text =
    _backup == null
        ? "Configure your backup system and add appliances to begin."
        : "Select your essential appliances and calculate your plan.";

}
private string BuildMonthlyEnergyReport()
{
    double tariff = _household?.TariffPerKwh ?? 0;

    var lines = new List<string>
    {
        "VOL TWISE ENERGY REPORT".Replace(" ", ""),
        DateTime.Now.ToString("MMMM yyyy"),
        "",
        "================================",
        "1. ENERGY OVERVIEW",
        "================================",
        $"Estimated monthly consumption: {MonthlyConsumptionKwh():N2} kWh",
        $"Estimated monthly electricity cost: {FormatCurrency(MonthlyCost())}"
    };

    if (tariff > 0)
    {
        lines.Add($"Configured electricity tariff: {FormatCurrency(tariff)} per kWh");
    }
    else
    {
        lines.Add("Electricity tariff: Not configured");
    }

    lines.Add("");
    lines.Add("================================");
    lines.Add("2. APPLIANCE BREAKDOWN");
    lines.Add("================================");

    if (_appliances.Count == 0)
    {
        lines.Add("No appliances have been recorded.");
    }
    else
    {
        foreach (var appliance in _appliances.OrderByDescending(
                     a => ApplianceMonthlyKwh(a)))
        {
            double monthlyKwh = ApplianceMonthlyKwh(appliance);
            double dailyKwh = monthlyKwh / 30.0;

            lines.Add("");
            lines.Add(appliance.Name);
            lines.Add(
                $"Rated power: {appliance.Watts:N0} W x {Math.Max(1, appliance.Quantity)}");

            lines.Add($"Estimated daily consumption: {dailyKwh:N2} kWh");
            lines.Add($"Estimated monthly consumption: {monthlyKwh:N2} kWh");

            if (tariff > 0)
            {
                lines.Add(
                    $"Estimated monthly cost: {FormatCurrency(ApplianceMonthlyCost(appliance))}");
            }
        }
    }

    lines.Add("");
    lines.Add("================================");
    lines.Add("3. ENERGY-SAVING RECOMMENDATIONS");
    lines.Add("================================");
    lines.Add(BuildEnergySavingRecommendations());

    lines.Add("");
    lines.Add("================================");
    lines.Add("4. POWER OUTAGE INTELLIGENCE");
    lines.Add("================================");
    lines.Add(BuildOutageIntelligence());

    lines.Add("");
    lines.Add("================================");
    lines.Add("5. BACKUP SYSTEM");
    lines.Add("================================");
    lines.Add(BuildBackupIntelligence());

    lines.Add("");
    lines.Add("================================");
    lines.Add("6. MONTHLY TREND");
    lines.Add("================================");
    lines.Add(BuildMonthlyTrend());

    lines.Add("");
    lines.Add("================================");
    lines.Add("REPORT NOTES");
    lines.Add("================================");
    lines.Add(
        "Consumption and cost figures are estimates based on recorded appliance ratings, usage patterns and configured tariff. They are not verified electricity meter readings or utility bills.");

    lines.Add(
        "Backup runtime is an estimate. Actual performance depends on battery condition, temperature, inverter losses, appliance startup surges and changing loads.");

    lines.Add("");
    lines.Add("Generated by Voltwise.");

    return string.Join(Environment.NewLine, lines);
}
private async void OnShareEnergyReportClicked(
    object sender,
    EventArgs e)
{
    try
    {
        string report = BuildMonthlyEnergyReport();

        await Share.Default.RequestAsync(
            new ShareTextRequest
            {
                Title = "Voltwise Monthly Energy Report",
                Text = report
            });
    }
    catch (Exception ex)
    {
        await DisplayAlert(
            "Report sharing failed",
            $"Unable to open the sharing menu. Details: {ex.Message}",
            "OK");
    }
}

    

    private double MonthlyConsumptionKwh()
    {
        return _appliances.Sum(appliance =>
            appliance.Watts *
            appliance.HoursPerDay *
            appliance.Quantity *
            30.0 / 1000.0);
    }

    private double MonthlyCost()
    {
        double tariff = _household?.TariffPerKwh ?? 0;

        return MonthlyConsumptionKwh() * tariff;
    }

    private double TotalOutageHours()
    {
        return _outages.Sum(outage => outage.DurationMinutes) / 60.0;
    }

        private double CalculateRuntime(
        double voltage,
        double capacityAh,
        int batteryCount,
        double usableCapacityPercent,
        double inverterEfficiencyPercent,
        double loadWatts)
    {
        if (voltage <= 0 ||
            capacityAh <= 0 ||
            batteryCount <= 0 ||
            usableCapacityPercent <= 0 ||
            usableCapacityPercent > 100 ||
            inverterEfficiencyPercent <= 0 ||
            inverterEfficiencyPercent > 100 ||
            loadWatts <= 0 ||
            !double.IsFinite(voltage) ||
            !double.IsFinite(capacityAh) ||
            !double.IsFinite(usableCapacityPercent) ||
            !double.IsFinite(inverterEfficiencyPercent) ||
            !double.IsFinite(loadWatts))
        {
            return 0;
        }

        double nominalWh =
            voltage * capacityAh * batteryCount;

        double usableEnergy =
            nominalWh *
            (usableCapacityPercent / 100.0) *
            (inverterEfficiencyPercent / 100.0);

        return usableEnergy / loadWatts;
    }

    private double BackupNominalEnergyWh()
    {
        if (_backup == null)
            return 0;

        return _backup.BatteryVoltage
            * _backup.CapacityAh
            * _backup.BatteryCount;
    }
    private double BackupUsableEnergyWh()
{
    if (_backup == null)
        return 0;

    return BackupNominalEnergyWh()
        * _backup.UsableCapacityPercent / 100.0;
}

private double BackupDeliveredEnergyWh()
{
    if (_backup == null)
        return 0;

    return BackupUsableEnergyWh()
        * _backup.InverterEfficiencyPercent / 100.0;
}

private double BackupInverterUtilisationPercent()
{
    if (_backup == null ||
        _backup.InverterRatedWatts <= 0)
    {
        return 0;
    }

    return _backup.LoadWatts
        / _backup.InverterRatedWatts * 100.0;
}

private bool IsBackupOverloaded()
{
    return _backup != null &&
        _backup.InverterRatedWatts > 0 &&
        _backup.LoadWatts > _backup.InverterRatedWatts;
}

    private static string FormatCurrency(double amount)
    {
        return "₦" + amount.ToString(
            "N2",
            CultureInfo.InvariantCulture);
    }

    private static string FormatRuntime(double hours)
    {
        if (hours <= 0)
            return "Not set";

        if (hours >= 24)
        {
            int days = (int)(hours / 24);
            double remainingHours = hours % 24;

            return $"{days}d {remainingHours:0.#}h";
        }

        if (hours >= 1)
            return $"{hours:0.##} hours";

        return $"{hours * 60:0.#} minutes";
    }
    
    // =========================================================
// ENERGY INTELLIGENCE
// =========================================================

private double ApplianceMonthlyKwh(ApplianceRecord appliance)
{
    return appliance.Watts
        * appliance.HoursPerDay
        * appliance.Quantity
        * 30.0 / 1000.0;
}

private double ApplianceMonthlyCost(ApplianceRecord appliance)
{
    return ApplianceMonthlyKwh(appliance)
        * (_household?.TariffPerKwh ?? 0);
}

private double ApplianceConsumptionShare(ApplianceRecord appliance)
{
    double total = MonthlyConsumptionKwh();

    if (total <= 0)
        return 0;

    return ApplianceMonthlyKwh(appliance) / total * 100.0;
}

private double PotentialMonthlySavings(
    ApplianceRecord appliance,
    double reducedHoursPerDay)
{
    if (reducedHoursPerDay < 0 ||
        reducedHoursPerDay > appliance.HoursPerDay)
    {
        return 0;
    }

    double savedHours =
        appliance.HoursPerDay - reducedHoursPerDay;

    double savedKwh =
        appliance.Watts
        * savedHours
        * appliance.Quantity
        * 30.0 / 1000.0;

    return savedKwh * (_household?.TariffPerKwh ?? 0);
}

private void UpdateMonthlySnapshot()
{
    DateTime now = DateTime.Now;

    string monthKey = now.ToString("yyyy-MM");

    var existing = _monthlySnapshots.FirstOrDefault(
        x => x.MonthKey == monthKey);

    if (existing == null)
    {
        existing = new MonthlySnapshot
        {
            MonthKey = monthKey,
            Month = new DateTime(now.Year, now.Month, 1)
        };

        _monthlySnapshots.Add(existing);
    }

    // Update the current month's estimate as records change.
    // Previous months remain untouched.
    existing.EstimatedConsumptionKwh = MonthlyConsumptionKwh();
    existing.EstimatedCost = MonthlyCost();
    existing.ApplianceCount = _appliances.Count;
    existing.OutageCount = _outages.Count(
        x => x.Date.Year == now.Year &&
             x.Date.Month == now.Month);

    existing.UpdatedAt = now;

    // Keep a bounded history.
    _monthlySnapshots = _monthlySnapshots
        .OrderByDescending(x => x.Month)
        .Take(24)
        .OrderBy(x => x.Month)
        .ToList();

    SaveMonthlySnapshots();
}

private MonthlySnapshot? GetPreviousMonthlySnapshot()
{
    DateTime currentMonth = new(
        DateTime.Now.Year,
        DateTime.Now.Month,
        1);

    return _monthlySnapshots
        .Where(x => x.Month < currentMonth)
        .OrderByDescending(x => x.Month)
        .FirstOrDefault();
}

private string BuildMonthlyTrend()
{
    MonthlySnapshot? previous = GetPreviousMonthlySnapshot();

    if (previous == null)
    {
        return "Historical comparison will become available as previous monthly records accumulate.";
    }

    double currentConsumption = MonthlyConsumptionKwh();
    double currentCost = MonthlyCost();

    string consumptionTrend = DescribeChange(
        currentConsumption,
        previous.EstimatedConsumptionKwh,
        "kWh");

    string costTrend = DescribeChange(
        currentCost,
        previous.EstimatedCost,
        "cost");

    return
        $"Compared with {previous.Month:MMM yyyy}:\n" +
        $"Consumption: {consumptionTrend}\n" +
        $"Estimated cost: {costTrend}";
}

private static string DescribeChange(
    double current,
    double previous,
    string metric)
{
    if (previous <= 0)
    {
        return current <= 0
            ? "No estimated change"
            : "No usable previous baseline";
    }

    double difference = current - previous;
    double percentage = Math.Abs(difference) / previous * 100.0;

    string direction = difference > 0
        ? "increased"
        : difference < 0
            ? "decreased"
            : "is unchanged";

    if (metric == "cost")
    {
        return difference == 0
            ? "unchanged"
            : $"{direction} by {FormatCurrency(Math.Abs(difference))} " +
              $"({percentage:N1}%)";
    }

    return difference == 0
        ? "unchanged"
        : $"{direction} by {Math.Abs(difference):N2} kWh " +
          $"({percentage:N1}%)";
}

private string BuildApplianceIntelligence()
{
    if (_appliances.Count == 0)
        return "No appliances recorded. Add appliances to see consumption rankings.";

    double totalConsumption = MonthlyConsumptionKwh();
    double tariff = _household?.TariffPerKwh ?? 0;

    var ranked = _appliances
        .Select(appliance => new
        {
            Appliance = appliance,
            Consumption = ApplianceMonthlyKwh(appliance),
            Cost = ApplianceMonthlyCost(appliance),
            Share = ApplianceConsumptionShare(appliance)
        })
        .OrderByDescending(x => x.Consumption)
        .ToList();

    var lines = new List<string>
    {
        $"Tracked appliances: {_appliances.Count}",
        $"Combined estimate: {totalConsumption:N2} kWh/month"
    };

    if (tariff <= 0)
    {
        lines.Add(
            "Set a positive electricity tariff to calculate appliance costs.");
    }

    lines.Add("");
    lines.Add("HIGHEST ESTIMATED CONSUMPTION");

    foreach (var item in ranked)
{
    double dailyConsumption = item.Consumption / 30.0;
    double weeklyConsumption = dailyConsumption * 7.0;

    lines.Add("");
    lines.Add(item.Appliance.Name);

    lines.Add(
        $"Daily: {dailyConsumption:N2} kWh");

    lines.Add(
        $"Weekly: {weeklyConsumption:N2} kWh");

    lines.Add(
        $"Monthly: {item.Consumption:N2} kWh " +
        $"({item.Share:N1}% of tracked consumption)");

    if (tariff > 0)
    {
        double dailyCost = item.Cost / 30.0;
        double weeklyCost = dailyCost * 7.0;

        lines.Add(
            $"Estimated daily cost: {FormatCurrency(dailyCost)}");

        lines.Add(
            $"Estimated weekly cost: {FormatCurrency(weeklyCost)}");

        lines.Add(
            $"Estimated monthly cost: {FormatCurrency(item.Cost)}");
    }
    else
    {
        lines.Add(
            "Set your electricity tariff to calculate running costs.");
    }
}
    var largest = ranked.First();

    if (largest.Appliance.HoursPerDay > 0)
    {
        double reducedHours = Math.Max(
            0,
            largest.Appliance.HoursPerDay - 1);

        double savings = PotentialMonthlySavings(
            largest.Appliance,
            reducedHours);

        lines.Add("");
        lines.Add("POTENTIAL SAVING");

        lines.Add(
            $"Reducing {largest.Appliance.Name} usage by one hour daily " +
            $"could save approximately {FormatCurrency(savings)} per month, " +
            "assuming its rated power and tariff remain unchanged.");
    }

    lines.Add("");
    lines.Add(
        "These are estimates based on entered wattage and usage, not live meter readings.");

    return string.Join("\n", lines);
}

private string BuildOutageIntelligence()
{
if (_outages.Count == 0)
{
return "No outages recorded yet. Log interruptions to build your outage history.";
}

DateTime today = DateTime.Today;
DateTime recentStart = today.AddDays(-29);
DateTime previousStart = today.AddDays(-59);

var recent = _outages
    .Where(x =>
        x.Date.Date >= recentStart &&
        x.Date.Date <= today)
    .ToList();

var previous = _outages
    .Where(x =>
        x.Date.Date >= previousStart &&
        x.Date.Date < recentStart)
    .ToList();

int recentMinutes = recent.Sum(
    x => Math.Max(0, x.DurationMinutes));

int previousMinutes = previous.Sum(
    x => Math.Max(0, x.DurationMinutes));

double averageMinutes = _outages.Average(
    x => (double)Math.Max(0, x.DurationMinutes));

var longest = _outages
    .OrderByDescending(x => x.DurationMinutes)
    .First();

var shortest = _outages
    .OrderBy(x => x.DurationMinutes)
    .First();

string recentSummary = recent.Count == 0
    ? "Last 30 days: No outages recorded."
    : $"Last 30 days: {recent.Count} outage(s), " +
      $"{recentMinutes / 60.0:N1} hours of recorded interruption.";

string comparison;

if (recent.Count == 0 && previous.Count == 0)
{
    comparison =
        "There is not enough recent history to compare outage activity.";
}
else if (previous.Count == 0)
{
    comparison = recent.Count > 0
        ? "Outages were recorded in the last 30 days, " +
          "but none were recorded in the preceding 30-day period."
        : "No outages were recorded in either 30-day period.";
}
else
{
    double change =
        (recentMinutes - previousMinutes) /
        (double)Math.Max(1, previousMinutes) * 100.0;

    if (recentMinutes > previousMinutes)
    {
        comparison =
            $"Recorded outage duration increased by " +
            $"{change:N0}% compared with the preceding 30 days.";
    }
    else if (recentMinutes < previousMinutes)
    {
        comparison =
            $"Recorded outage duration decreased by " +
            $"{Math.Abs(change):N0}% compared with the preceding 30 days.";
    }
    else
    {
        comparison =
            "Recorded outage duration was unchanged compared with the preceding 30 days.";
    }
}

return
    $"{recentSummary}\n\n" +
    $"PREVIOUS 30 DAYS\n" +
    $"Outages: {previous.Count}\n" +
    $"Recorded duration: {previousMinutes / 60.0:N1} hours\n\n" +
    $"OUTAGE HISTORY\n" +
    $"Total recorded outages: {_outages.Count}\n" +
    $"Average duration: {averageMinutes:N0} minutes\n" +
    $"Longest outage: {longest.DurationMinutes:N0} minutes " +
    $"on {longest.Date:dd MMM yyyy}\n" +
    $"Shortest recorded outage: {shortest.DurationMinutes:N0} minutes\n\n" +
    $"TREND\n{comparison}\n\n" +
    "These statistics describe recorded outages only. " +
    "They do not predict future electricity supply.";

}
private string BuildBackupIntelligence()
{
if (_backup == null)
{
return "No backup configuration saved. Configure your battery, inverter and expected load to estimate runtime.";
}

double nominalWh = BackupNominalEnergyWh();
double usableWh = BackupUsableEnergyWh();
double deliveredWh = BackupDeliveredEnergyWh();
double utilisation = BackupInverterUtilisationPercent();

var lines = new List<string>
{
    $"Nominal battery energy: {nominalWh:N0} Wh",
    $"Usable battery energy: {usableWh:N0} Wh",
    $"Estimated energy delivered after inverter losses: {deliveredWh:N0} Wh",
    $"Connected load: {_backup.LoadWatts:N0} W",
    $"Inverter rating: {_backup.InverterRatedWatts:N0} W",
    $"Inverter utilisation: {utilisation:N1}%",
    $"Estimated runtime: {FormatRuntime(_backup.EstimatedRuntimeHours)}"
};

if (IsBackupOverloaded())
{
    double excessWatts =
        _backup.LoadWatts - _backup.InverterRatedWatts;

    lines.Add("");
    lines.Add("INVERTER OVERLOAD WARNING");

    lines.Add(
        $"The configured load exceeds the inverter rating by {excessWatts:N0} W. " +
        "Reduce the connected load before relying on this configuration.");
}
else if (utilisation >= 90)
{
    lines.Add("");
    lines.Add(
        "The configured load is close to the inverter's rated output. " +
        "Allow for startup surges and appliance power variations.");
}
else
{
    lines.Add("");
    lines.Add(
        "The configured load is within the inverter's stated continuous rating. " +
        "Check appliance startup surges and any manufacturer-specific limits.");
}

if (_backup.EstimatedRuntimeHours < 1)
{
    lines.Add(
        "Battery readiness: estimated runtime is under one hour. " +
        "Consider reducing the connected load.");
}
else if (_backup.EstimatedRuntimeHours < 3)
{
    lines.Add(
        "Battery readiness: runtime is relatively short. " +
        "Prioritise essential appliances.");
}
else
{
    lines.Add(
        "Battery readiness: review whether the estimated runtime meets your outage needs.");
}

lines.Add("");
lines.Add(
    "Runtime is an estimate, not a guarantee. Actual results depend on battery condition, chemistry, temperature, discharge behaviour, inverter limits and changing loads.");

return string.Join("\n", lines);

}
    // =========================================================
    // DASHBOARD REFRESH
    // =========================================================

    private void RefreshDashboard()
{
    UpdateMonthlySnapshot();

    RefreshHousehold();
    RefreshMetrics();
    RefreshApplianceList();
    RefreshOutageList();
    RefreshBackup();
    RefreshInsights();
    RefreshRecentActivity();
    RefreshBudgetCard();

    HeaderDateLabel.Text =
        DateTime.Now.ToString("dd MMM").ToUpperInvariant();

    HeaderStatusLabel.Text = _household == null
        ? "Set up your household to get started."
        : "Your energy dashboard is ready.";
}

    private void RefreshHousehold()
    {
        if (_household == null)
        {
            LocationLabel.Text = "Setup required";
            SupplyLabel.Text = "Configure your electricity tariff";

            ProfileStatusLabel.Text =
                "Add your household location and tariff to improve your estimates.";

            GreetingLabel.Text =
                "Your household. Your energy. Clearer decisions.";

            return;
        }

        LocationLabel.Text = _household.Location;
        SupplyLabel.Text =
            $"{_household.SupplyType} · {FormatCurrency(_household.TariffPerKwh)}/kWh";

        ProfileStatusLabel.Text =
            $"Profile updated {_household.UpdatedAt:dd MMM yyyy}.";

        GreetingLabel.Text =
            $"Your energy overview for {_household.Location}.";
    }

    private void RefreshMetrics()
    {
        double consumption = MonthlyConsumptionKwh();
        double cost = MonthlyCost();

        MonthlyCostLabel.Text = FormatCurrency(cost);
        ApplianceCountLabel.Text = _appliances.Count.ToString();
        OutageCountLabel.Text = _outages.Count.ToString();

        RuntimeLabel.Text = _backup == null
            ? "Not set"
            : FormatRuntime(_backup.EstimatedRuntimeHours);

        MonthlyConsumptionLabel.Text = $"{consumption:N2} kWh";
        UsageCostLabel.Text = FormatCurrency(cost);

        UsageTariffLabel.Text = _household == null
            ? "Set up your household profile to calculate a tariff-based estimate."
            : $"Tariff: {FormatCurrency(_household.TariffPerKwh)} per kWh.";

        InsightsConsumptionLabel.Text =
            $"{consumption:N2} kWh per month";

        InsightsCostLabel.Text =
            $"Estimated monthly cost: {FormatCurrency(cost)}";

        InsightsApplianceLabel.Text = _appliances.Count == 0
            ? "No appliances recorded."
            : $"{_appliances.Count} appliance record(s) are included in this estimate.";
    }

    private void RefreshApplianceList()
    {
        ApplianceList.Children.Clear();

        if (_appliances.Count == 0)
        {
            ApplianceList.Children.Add(
                CreateEmptyState(
                    "No appliances yet",
                    "Add a device to start estimating your household consumption."));

            return;
        }

        foreach (ApplianceRecord appliance in _appliances)
        {
            ApplianceList.Children.Add(
                CreateApplianceCard(appliance));
        }
    }

    private View CreateApplianceCard(ApplianceRecord appliance)
    {
        var outer = new Border
        {
            Padding = 13,
            StrokeThickness = 1,
            Stroke = GetThemeColor(
                Color.FromArgb("#E0E5E9"),
                Color.FromArgb("#343B44")),
            BackgroundColor = GetThemeColor(
                Color.FromArgb("#F8FAFB"),
                Color.FromArgb("#252C34")),
            StrokeShape = new RoundRectangle
            {
                CornerRadius = 17
            }
        };

        var content = new VerticalStackLayout
        {
            Spacing = 10
        };

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };

        var details = new VerticalStackLayout
        {
            Spacing = 4
        };

        details.Children.Add(new Label
        {
            Text = appliance.Name,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold
        });

        details.Children.Add(new Label
        {
            Text =
                $"{appliance.Watts:N0} W · {appliance.Quantity} unit(s) · " +
                $"{appliance.HoursPerDay:N1} h/day",
            FontSize = 11,
            TextColor = GetThemeColor(
                Color.FromArgb("#66717E"),
                Color.FromArgb("#A2ACB8"))
        });

        header.Children.Add(details);

        var estimatedEnergy =
    ApplianceMonthlyKwh(appliance);

var estimatedCost =
    ApplianceMonthlyCost(appliance);

var consumptionShare =
    ApplianceConsumptionShare(appliance);

var estimate = new Label
{
    Text =
        $"{estimatedEnergy:N1} kWh/mo\n" +
        $"{consumptionShare:N0}% usage\n" +
        $"{FormatCurrency(estimatedCost)}/mo",

    FontSize = 11,
    FontAttributes = FontAttributes.Bold,

    TextColor = GetThemeColor(
        Color.FromArgb("#315D29"),
        Color.FromArgb("#9BE564")),

    VerticalOptions = LayoutOptions.Center,
    HorizontalTextAlignment = TextAlignment.End
};

        Grid.SetColumn(estimate, 1);
        header.Children.Add(estimate);

        content.Children.Add(header);

        var buttons = new HorizontalStackLayout
        {
            Spacing = 8
        };

        var editButton = CreateSmallButton(
            "Edit",
            false);

        editButton.Clicked += async (_, _) =>
            await ShowApplianceModal(appliance);

        var deleteButton = CreateSmallButton(
            "Delete",
            true);

        deleteButton.Clicked += async (_, _) =>
            await ConfirmDeleteAppliance(appliance);

        buttons.Children.Add(editButton);
        buttons.Children.Add(deleteButton);

        content.Children.Add(buttons);

        outer.Content = content;

        return outer;
    }

    private void RefreshOutageList()
    {
        OutageList.Children.Clear();

        if (_outages.Count == 0)
        {
            OutageList.Children.Add(
                CreateEmptyState(
                    "No outages recorded",
                    "Record an interruption to keep track of its duration."));

            InsightsOutageLabel.Text = "No outages recorded.";

            InsightsDurationLabel.Text =
                "Total recorded outage duration: 0 minutes";

            return;
        }

        InsightsOutageLabel.Text =
            $"{_outages.Count} recorded outage(s).";

        int totalMinutes = _outages.Sum(
            outage => outage.DurationMinutes);

        InsightsDurationLabel.Text =
            $"Total recorded outage duration: {totalMinutes:N0} minutes " +
            $"({totalMinutes / 60.0:N1} hours).";

        foreach (OutageRecord outage in
                 _outages.OrderByDescending(x => x.RecordedAt))
        {
            OutageList.Children.Add(
                CreateOutageCard(outage));
        }
    }

    private View CreateOutageCard(OutageRecord outage)
    {
        var card = new Border
        {
            Padding = 13,
            StrokeThickness = 1,
            Stroke = GetThemeColor(
                Color.FromArgb("#E0E5E9"),
                Color.FromArgb("#343B44")),
            BackgroundColor = GetThemeColor(
                Color.FromArgb("#F8FAFB"),
                Color.FromArgb("#252C34")),
            StrokeShape = new RoundRectangle
            {
                CornerRadius = 17
            }
        };

        var layout = new VerticalStackLayout
        {
            Spacing = 8
        };

        layout.Children.Add(new Label
        {
            Text = outage.Date.ToString("dd MMM yyyy"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 14
        });

        layout.Children.Add(new Label
        {
            Text =
                $"Started {DateTime.Today.Add(outage.StartTime):hh:mm tt} · " +
                $"{outage.DurationMinutes} minutes",
            FontSize = 12,
            TextColor = GetThemeColor(
                Color.FromArgb("#66717E"),
                Color.FromArgb("#A2ACB8"))
        });

        if (!string.IsNullOrWhiteSpace(outage.Notes))
        {
            layout.Children.Add(new Label
            {
                Text = outage.Notes,
                FontSize = 12,
                LineBreakMode = LineBreakMode.WordWrap
            });
        }

        var deleteButton = CreateSmallButton(
            "Delete outage",
            true);

        deleteButton.HorizontalOptions = LayoutOptions.Start;

        deleteButton.Clicked += async (_, _) =>
            await ConfirmDeleteOutage(outage);

        layout.Children.Add(deleteButton);

        card.Content = layout;

        return card;
    }

    private void RefreshBackup()
{
if (_backup == null)
{
BackupRuntimeLabel.Text = "Not configured";

    BackupDetailsLabel.Text =
        "Configure your battery, inverter and connected load to calculate runtime.";

    InsightsBackupLabel.Text =
        "No backup configuration saved.";

    return;
}

double utilisation = BackupInverterUtilisationPercent();

BackupRuntimeLabel.Text =
    FormatRuntime(_backup.EstimatedRuntimeHours);

string inverterStatus = IsBackupOverloaded()
    ? "OVERLOADED"
    : utilisation >= 90
        ? "NEAR CAPACITY"
        : "WITHIN RATING";

BackupDetailsLabel.Text =
    $"{_backup.BatteryCount} battery/batteries · " +
    $"{_backup.BatteryVoltage:N0} V · " +
    $"{_backup.CapacityAh:N0} Ah each\n" +
    $"Load: {_backup.LoadWatts:N0} W · " +
    $"Inverter: {_backup.InverterRatedWatts:N0} W\n" +
    $"Inverter utilisation: {utilisation:N1}% · {inverterStatus}\n" +
    $"Usable capacity: {_backup.UsableCapacityPercent:N0}% · " +
    $"Efficiency: {_backup.InverterEfficiencyPercent:N0}%";

InsightsBackupLabel.Text =
    BuildBackupIntelligence();

}

private async Task CheckBackupAlertsAsync()
{
if (_backup == null)
return;

double runtimeMinutes =
    _backup.EstimatedRuntimeHours * 60;

if (double.IsNaN(runtimeMinutes) ||
    double.IsInfinity(runtimeMinutes) ||
    runtimeMinutes < 0)
{
    return;
}

bool overloaded = IsBackupOverloaded();

string alertKey = BackupAlertsKey;

string savedAlerts = Preferences.Get(
    alertKey,
    "");

var sentAlerts = new HashSet<string>(
    savedAlerts.Split(
        ',',
        StringSplitOptions.RemoveEmptyEntries));

var currentAlerts = new HashSet<string>();

if (overloaded)
    currentAlerts.Add("OVERLOAD");

if (runtimeMinutes < 30)
    currentAlerts.Add("RUNTIME_30");

if (runtimeMinutes < 10)
    currentAlerts.Add("RUNTIME_10");

// Remove alerts for conditions that have recovered.
sentAlerts.IntersectWith(currentAlerts);

if (overloaded &&
    !sentAlerts.Contains("OVERLOAD"))
{
    await LocalNotificationCenter.Current.Show(
        new NotificationRequest
        {
            NotificationId = 3010,
            Title = "Inverter overload warning",
            Description =
                "Your estimated connected load exceeds the inverter rating. Reduce the connected load."
        });

    sentAlerts.Add("OVERLOAD");
}

if (runtimeMinutes < 10 &&
    !sentAlerts.Contains("RUNTIME_10"))
{
    await LocalNotificationCenter.Current.Show(
        new NotificationRequest
        {
            NotificationId = 3011,
            Title = "Critical backup runtime",
            Description =
                "Estimated backup runtime is below 10 minutes."
        });

    sentAlerts.Add("RUNTIME_10");
}
else if (runtimeMinutes < 30 &&
         !sentAlerts.Contains("RUNTIME_30"))
{
    await LocalNotificationCenter.Current.Show(
        new NotificationRequest
        {
            NotificationId = 3012,
            Title = "Low backup runtime",
            Description =
                "Estimated backup runtime is below 30 minutes."
        });

    sentAlerts.Add("RUNTIME_30");
}

Preferences.Set(
    alertKey,
    string.Join(",", sentAlerts));

}

private async Task ScheduleDailyReminderAsync()
{
bool permissionGranted =
await LocalNotificationCenter.Current
.RequestNotificationPermission();

if (!permissionGranted)
    return;

DateTimeOffset now = DateTimeOffset.Now;

DateTimeOffset reminderTime =
    new DateTimeOffset(
        now.Year,
        now.Month,
        now.Day,
        9,
        0,
        0,
        now.Offset);

if (reminderTime <= now)
    reminderTime = reminderTime.AddDays(1);

await LocalNotificationCenter.Current.Show(
    new NotificationRequest
    {
        NotificationId = DailyReminderNotificationId,
        Title = "Voltwise daily check-in",
        Description =
            "Review your electricity usage, monthly budget and backup power readiness.",
        Schedule =
        {
            NotifyTime = reminderTime,
            RepeatType = NotificationRepeat.Daily
        }
    });

}
private string BuildEnergySavingRecommendations()
{
double tariff = _household?.TariffPerKwh ?? 0;
double budget = _monthlyBudget;

double runtime = _backup?.EstimatedRuntimeHours ?? -1;

var recommendations = EnergySavingsEngine.Analyse(
    _appliances.Select(a => (
        Name: a.Name,
        Watts: a.Watts * Math.Max(1, a.Quantity),
        HoursPerDay: a.HoursPerDay)),
    tariff,
    budget,
    MonthlyConsumptionKwh(),
    MonthlyCost(),
    runtime);

return string.Join(
    "\n\n",
    recommendations.Select(r =>
    {
        string result =
            $"[{r.Priority.ToUpperInvariant()}] {r.Title}\n" +
            r.Description;

        if (r.EstimatedMonthlySavings > 0)
        {
            result +=
                $"\nEstimated savings: " +
                $"{FormatCurrency(r.EstimatedMonthlySavings)}/month";
        }

        return result;
    }));

}

    private void RefreshInsights()
{
RefreshOutageList();

InsightsConsumptionLabel.Text =
    $"{MonthlyConsumptionKwh():N2} kWh per month";

InsightsApplianceLabel.Text =
    BuildApplianceIntelligence();

InsightsOutageLabel.Text =
    BuildOutageIntelligence();

InsightsBackupLabel.Text =
    BuildBackupIntelligence();

InsightsCostLabel.Text =
    $"Estimated monthly cost: {FormatCurrency(MonthlyCost())}\n\n" +
    BuildMonthlyTrend();

EnergyRecommendationsLabel.Text =
    BuildEnergySavingRecommendations();

}
    private void RefreshRecentActivity()
    {
        var activities = new List<(DateTime Date, string Text)>();

        if (_household != null)
        {
            activities.Add((
                _household.UpdatedAt,
                $"Household profile updated · {_household.Location}"));
        }

        activities.AddRange(
            _appliances.Select(appliance =>
                (
                    appliance.UpdatedAt,
                    $"Appliance saved · {appliance.Name}"
                )));

        activities.AddRange(
            _outages.Select(outage =>
                (
                    outage.RecordedAt,
                    $"Outage recorded · {outage.DurationMinutes} minutes"
                )));

        if (_backup != null)
        {
            activities.Add((
                _backup.UpdatedAt,
                $"Backup planner updated · {FormatRuntime(_backup.EstimatedRuntimeHours)}"));
        }

        var latest = activities
            .OrderByDescending(x => x.Date)
            .Take(4)
            .ToList();

        RecentActivityLabel.Text = latest.Count == 0
            ? "Your recent activity will appear here."
            : string.Join(
                "\n\n",
                latest.Select(x =>
                    $"• {x.Text}\n  {x.Date:dd MMM, hh:mm tt}"));
    }

    // =========================================================
    // NAVIGATION
    // =========================================================

    private async Task SelectTabAsync(string tab)
    {
        if (_isAnimating || _selectedTab == tab)
            return;

        _isAnimating = true;

        try
        {
            View? current = GetScreen(_selectedTab);
            View? next = GetScreen(tab);

            if (current == null || next == null)
                return;

            next.IsVisible = true;
            next.Opacity = 0;
            next.TranslationY = 8;

            await Task.WhenAll(
                current.FadeTo(0, 100),
                next.FadeTo(1, 180),
                next.TranslateTo(0, 0, 180));

            current.IsVisible = false;
            current.TranslationY = 0;

            _selectedTab = tab;

            UpdateNavigation(tab);
        }
        finally
        {
            _isAnimating = false;
        }
    }

    private View? GetScreen(string tab)
    {
        return tab switch
        {
            "Home" => HomeScreen,
            "Usage" => UsageScreen,
            "Backup" => BackupScreen,
            "Insights" => InsightsScreen,
            _ => null
        };
    }

    private void UpdateNavigation(string selected)
    {
        SetNavigationItem(
            HomeNavButton,
            HomeNavIcon,
            HomeNavLabel,
            selected == "Home");

        SetNavigationItem(
            UsageNavButton,
            UsageNavIcon,
            UsageNavLabel,
            selected == "Usage");

        SetNavigationItem(
            BackupNavButton,
            BackupNavIcon,
            BackupNavLabel,
            selected == "Backup");

        SetNavigationItem(
            InsightsNavButton,
            InsightsNavIcon,
            InsightsNavLabel,
            selected == "Insights");
    }

    private void SetNavigationItem(
    Button button,
    Microsoft.Maui.Controls.Shapes.Path icon,
    Label label,
    bool selected)
{
    Color accent = Color.FromArgb("#9BE564");

    Color muted = GetThemeColor(
        Color.FromArgb("#66717E"),
        Color.FromArgb("#A2ACB8"));

    icon.Stroke = selected ? accent : muted;
    label.TextColor = selected ? accent : muted;

    // Keep the button behind the icon and label.
    button.BackgroundColor = selected
        ? GetThemeColor(
            Color.FromArgb("#F0F7E9"),
            Color.FromArgb("#293629"))
        : Colors.Transparent;

    button.Scale = selected ? 1.02 : 1.0;
}    private async void OnHomeTapped(
        object sender,
        EventArgs e)
    {
        await SelectTabAsync("Home");
    }

    private async void OnUsageTapped(
        object sender,
        EventArgs e)
    {
        await SelectTabAsync("Usage");
    }

    private async void OnBackupTapped(
        object sender,
        EventArgs e)
    {
        await SelectTabAsync("Backup");
    }

    private async void OnInsightsTapped(
        object sender,
        EventArgs e)
    {
        await SelectTabAsync("Insights");
    }

    private async void OnBackupClicked(
        object sender,
        EventArgs e)
    {
        await SelectTabAsync("Backup");
    }

    private async void OnInsightsClicked(
        object sender,
        EventArgs e)
    {
        await SelectTabAsync("Insights");
    }

    // =========================================================
    // THEME
    // =========================================================

    private void OnThemeToggleClicked(
        object sender,
        EventArgs e)
    {
        AppTheme currentTheme = Application.Current!.RequestedTheme;

        AppTheme effectiveTheme =
            Application.Current.UserAppTheme == AppTheme.Unspecified
                ? currentTheme
                : Application.Current.UserAppTheme;

        AppTheme newTheme = effectiveTheme == AppTheme.Dark
            ? AppTheme.Light
            : AppTheme.Dark;

        Application.Current.UserAppTheme = newTheme;

        Preferences.Set(
            ThemeKey,
            newTheme == AppTheme.Dark ? "Dark" : "Light");

        UpdateNavigation(_selectedTab);
    }

    // =========================================================
    // MODAL INFRASTRUCTURE
    // =========================================================

    private void InitializeModalSystem()
    {
        ModalOverlay.IsVisible = false;
        ModalOverlay.Opacity = 0;
    }

    private async Task OpenModalAsync(
        string type,
        string title,
        string subtitle,
        string eyebrow = "VOLTWISE")
    {
        if (_isModalOpen)
            return;

        _isModalOpen = true;
        _modalType = type;

        ModalEyebrow.Text = eyebrow;
        ModalTitle.Text = title;
        ModalSubtitle.Text = subtitle;

        ModalContent.Children.Clear();
        ModalActions.Children.Clear();
        _modalEntries.Clear();

        ModalOverlay.IsVisible = true;
        ModalOverlay.Opacity = 0;

        await ModalOverlay.FadeTo(1, 160);
    }

    private async Task CloseModalAsync()
    {
        if (!_isModalOpen)
            return;

        await ModalOverlay.FadeTo(0, 120);

        ModalOverlay.IsVisible = false;
        ModalContent.Children.Clear();
        ModalActions.Children.Clear();
        _modalEntries.Clear();

        _isModalOpen = false;
        _isSaving = false;
        _modalType = "";
    }

    private async void OnCloseModalClicked(
        object sender,
        EventArgs e)
    {
        await CloseModalAsync();
    }

    private void AddModalLabel(
        string text,
        bool muted = false)
    {
        ModalContent.Children.Add(new Label
        {
            Text = text,
            FontSize = muted ? 12 : 13,
            FontAttributes = muted
                ? FontAttributes.None
                : FontAttributes.Bold,
            TextColor = muted
                ? GetThemeColor(
                    Color.FromArgb("#66717E"),
                    Color.FromArgb("#A2ACB8"))
                : GetThemeColor(
                    Color.FromArgb("#20262D"),
                    Color.FromArgb("#F3F5F7"))
        });
    }

    private Entry AddModalEntry(
        string key,
        string label,
        string value = "",
        string placeholder = "",
        Keyboard? keyboard = null)
    {
        AddModalLabel(label);

        var entry = new Entry
        {
            Text = value,
            Placeholder = placeholder,
            Keyboard = keyboard ?? Keyboard.Default,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            BackgroundColor = GetThemeColor(
                Color.FromArgb("#F4F6F8"),
                Color.FromArgb("#252C34")),
            TextColor = GetThemeColor(
                Color.FromArgb("#20262D"),
                Color.FromArgb("#F3F5F7")),
            PlaceholderColor = GetThemeColor(
                Color.FromArgb("#8A949F"),
                Color.FromArgb("#A2ACB8")),
            HeightRequest = 48
        };

        ModalContent.Children.Add(entry);
        _modalEntries[key] = entry;

        return entry;
    }

    private Picker AddModalPicker(
        string label,
        IEnumerable<string> options,
        string selected)
    {
        AddModalLabel(label);

        var picker = new Picker
        {
            ItemsSource = options.ToList(),
            Title = "Choose an option",
            BackgroundColor = GetThemeColor(
                Color.FromArgb("#F4F6F8"),
                Color.FromArgb("#252C34")),
            TextColor = GetThemeColor(
                Color.FromArgb("#20262D"),
                Color.FromArgb("#F3F5F7")),
            HeightRequest = 48
        };

        int index = picker.ItemsSource.IndexOf(selected);

        if (index >= 0)
            picker.SelectedIndex = index;

        ModalContent.Children.Add(picker);

        return picker;
    }

    private Editor AddModalEditor(
        string label,
        string value,
        string placeholder)
    {
        AddModalLabel(label);

        var editor = new Editor
        {
            Text = value,
            Placeholder = placeholder,
            AutoSize = EditorAutoSizeOption.TextChanges,
            MinimumHeightRequest = 90,
            BackgroundColor = GetThemeColor(
                Color.FromArgb("#F4F6F8"),
                Color.FromArgb("#252C34")),
            TextColor = GetThemeColor(
                Color.FromArgb("#20262D"),
                Color.FromArgb("#F3F5F7"))
        };

        ModalContent.Children.Add(editor);

        return editor;
    }

    private void AddModalButton(
        string text,
        EventHandler clicked,
        bool destructive = false,
        bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 16,
            Padding = new Thickness(16, 13),
            MinimumHeightRequest = 48,
            HorizontalOptions = LayoutOptions.Fill
        };

        if (primary)
        {
            button.BackgroundColor = Color.FromArgb("#9BE564");
            button.TextColor = Color.FromArgb("#172116");
        }
        else if (destructive)
        {
            button.BackgroundColor = Colors.Transparent;
            button.TextColor = Color.FromArgb("#E45B5B");
            button.BorderColor = Color.FromArgb("#E45B5B");
            button.BorderWidth = 1;
        }
        else
        {
            button.BackgroundColor = GetThemeColor(
                Color.FromArgb("#EAEFF2"),
                Color.FromArgb("#252C34"));

            button.TextColor = GetThemeColor(
                Color.FromArgb("#20262D"),
                Color.FromArgb("#F3F5F7"));
        }

        button.Clicked += clicked;
        ModalActions.Children.Add(button);
    }

    private void AddCancelButton()
    {
        AddModalButton(
            "Cancel",
            async (_, _) => await CloseModalAsync());
    }

    private View CreateEmptyState(
        string title,
        string description)
    {
        var layout = new VerticalStackLayout
        {
            Spacing = 5,
            Padding = new Thickness(4, 8)
        };

        layout.Children.Add(new Label
        {
            Text = title,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold
        });

        layout.Children.Add(new Label
        {
            Text = description,
            FontSize = 12,
            TextColor = GetThemeColor(
                Color.FromArgb("#66717E"),
                Color.FromArgb("#A2ACB8"))
        });

        return layout;
    }

    private Button CreateSmallButton(
        string text,
        bool destructive)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 11,
            Padding = new Thickness(12, 7),
            CornerRadius = 11,
            MinimumHeightRequest = 34,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 1,
            BorderColor = destructive
                ? Color.FromArgb("#E45B5B")
                : GetThemeColor(
                    Color.FromArgb("#D0D8DE"),
                    Color.FromArgb("#46515D")),
            TextColor = destructive
                ? Color.FromArgb("#E45B5B")
                : GetThemeColor(
                    Color.FromArgb("#20262D"),
                    Color.FromArgb("#F3F5F7"))
        };

        return button;
    }

    private static Color GetThemeColor(
        Color light,
        Color dark)
    {
        AppTheme theme =
            Application.Current?.RequestedTheme ?? AppTheme.Light;

        if (Application.Current?.UserAppTheme == AppTheme.Dark)
            theme = AppTheme.Dark;

        if (Application.Current?.UserAppTheme == AppTheme.Light)
            theme = AppTheme.Light;

        return theme == AppTheme.Dark ? dark : light;
    }

    private string GetEntryValue(string key)
    {
        return _modalEntries.TryGetValue(key, out Entry? entry)
            ? entry.Text?.Trim() ?? string.Empty
            : string.Empty;
    }

    private bool TryReadDouble(
        string key,
        out double value)
    {
        string text = GetEntryValue(key)
            .Replace(",", ".");

        return double.TryParse(
                   text,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out value)
               && double.IsFinite(value);
    }

    private bool TryReadInt(
        string key,
        out int value)
    {
        return int.TryParse(
            GetEntryValue(key),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }

    private async Task ShowValidationError(string message)
    {
        await CloseModalAsync();

        await DisplayAlert(
            "Check your information",
            message,
            "OK");
    }

    // =========================================================
    // HOUSEHOLD PROFILE
    // =========================================================

    private async void OnSetupClicked(
        object sender,
        EventArgs e)
    {
        await ShowHouseholdModal();
    }

    private async Task ShowHouseholdModal()
    {
        await OpenModalAsync(
            "household",
            "Your household",
            "Personalise your energy estimates.",
            "PROFILE SETTINGS");

        AddModalEntry(
            "location",
            "Household location",
            _household?.Location ?? "",
            "e.g. Osogbo",
            Keyboard.Text);

        AddModalEntry(
            "tariff",
            "Electricity tariff (₦ per kWh)",
            _household == null
                ? ""
                : _household.TariffPerKwh.ToString(
                    CultureInfo.InvariantCulture),
            "e.g. 150",
            Keyboard.Numeric);

        AddModalPicker(
            "Electricity supply type",
            new[]
            {
                "Grid",
                "Grid + solar",
                "Solar",
                "Generator",
                "Mixed"
            },
            _household?.SupplyType ?? "Grid");

        AddModalButton(
            "Save household profile",
            async (_, _) => await SaveHouseholdModal(),
            primary: true);

        if (_household != null)
        {
            AddModalButton(
                "Reset household profile",
                async (_, _) => await ConfirmResetHousehold(),
                destructive: true);
        }

        AddCancelButton();
    }

    private async Task SaveHouseholdModal()
    {
        if (_isSaving)
            return;

        string location = GetEntryValue("location");

        if (string.IsNullOrWhiteSpace(location))
        {
            await DisplayAlert(
                "Location required",
                "Enter your household location.",
                "OK");

            return;
        }

        if (!TryReadDouble("tariff", out double tariff) ||
            tariff < 0)
        {
            await DisplayAlert(
                "Invalid tariff",
                "Enter a valid tariff of zero or more naira per kWh.",
                "OK");

            return;
        }

        Picker? supplyPicker = ModalContent.Children
            .OfType<Picker>()
            .FirstOrDefault();

        string supply = supplyPicker?.SelectedItem?.ToString()
            ?? "Grid";

        _isSaving = true;

        _household = new HouseholdSetup
        {
            Location = location,
            TariffPerKwh = tariff,
            SupplyType = supply,
            UpdatedAt = DateTime.Now
        };

        SaveHousehold();

        await CloseModalAsync();

        RefreshDashboard();
    }

    private async Task ConfirmResetHousehold()
{
    await CloseModalAsync();

    await ShowConfirmation(
        "Reset household profile?",
        "Your saved location, tariff and supply type will be removed. Your appliances and outage records will remain.",
        async () =>
        {
            _household = null;
            SaveHousehold();
            RefreshDashboard();
        });
}

    private async void OnResetProfileClicked(
        object sender,
        EventArgs e)
    {
        if (_household == null)
        {
            await ShowHouseholdModal();
            return;
        }

        await ConfirmResetHousehold();
    }

    // =========================================================
    // APPLIANCES
    // =========================================================

    private async void OnRecordUsageClicked(
        object sender,
        EventArgs e)
    {
        await ShowApplianceModal(null);
    }

    private async Task ShowApplianceModal(
        ApplianceRecord? appliance)
    {
        bool editing = appliance != null;

        _editingApplianceId = appliance?.Id;

        await OpenModalAsync(
            editing ? "edit_appliance" : "add_appliance",
            editing ? "Edit appliance" : "Add appliance",
            "Enter the details used for your energy estimate.",
            "APPLIANCE LEDGER");

        AddModalEntry(
            "name",
            "Appliance name",
            appliance?.Name ?? "",
            "e.g. Refrigerator");

        AddModalEntry(
            "watts",
            "Rated power (watts)",
            appliance?.Watts.ToString(
                CultureInfo.InvariantCulture) ?? "",
            "e.g. 150",
            Keyboard.Numeric);

        AddModalEntry(
            "hours",
            "Average usage per day (hours)",
            appliance?.HoursPerDay.ToString(
                CultureInfo.InvariantCulture) ?? "",
            "e.g. 8",
            Keyboard.Numeric);

        AddModalEntry(
            "quantity",
            "Number of units",
            appliance?.Quantity.ToString(
                CultureInfo.InvariantCulture) ?? "1",
            "e.g. 1",
            Keyboard.Numeric);

        AddModalLabel(
            "The estimate assumes a 30-day month.",
            muted: true);

        AddModalButton(
            editing ? "Save changes" : "Add appliance",
            async (_, _) => await SaveApplianceModal(),
            primary: true);

        AddCancelButton();
    }

    private async Task SaveApplianceModal()
    {
        if (_isSaving)
            return;

        string name = GetEntryValue("name");

        if (string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlert(
                "Name required",
                "Enter an appliance name.",
                "OK");

            return;
        }

        if (!TryReadDouble("watts", out double watts) ||
            watts <= 0)
        {
            await DisplayAlert(
                "Invalid wattage",
                "Enter a wattage greater than zero.",
                "OK");

            return;
        }

        if (!TryReadDouble("hours", out double hours) ||
            hours < 0 ||
            hours > 24)
        {
            await DisplayAlert(
                "Invalid daily usage",
                "Enter a number between 0 and 24 hours.",
                "OK");

            return;
        }

        if (!TryReadInt("quantity", out int quantity) ||
            quantity < 1 ||
            quantity > 10000)
        {
            await DisplayAlert(
                "Invalid quantity",
                "Enter a quantity of at least one.",
                "OK");

            return;
        }

        _isSaving = true;

        ApplianceRecord? existing = _appliances.FirstOrDefault(
            x => x.Id == _editingApplianceId);

        if (existing != null)
        {
            existing.Name = name;
            existing.Watts = watts;
            existing.HoursPerDay = hours;
            existing.Quantity = quantity;
            existing.UpdatedAt = DateTime.Now;
        }
        else
        {
            _appliances.Add(new ApplianceRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Watts = watts,
                HoursPerDay = hours,
                Quantity = quantity,
                UpdatedAt = DateTime.Now
            });
        }

        SaveAppliances();

await CloseModalAsync();

RefreshDashboard();

await CheckBudgetAlertsAsync();
    }

    private async Task ConfirmDeleteAppliance(
        ApplianceRecord appliance)
    {
        await ShowConfirmation(
            $"Delete {appliance.Name}?",
            "This appliance will be removed from your ledger and monthly estimates.",
            async () =>
            {
                _appliances.RemoveAll(
                    x => x.Id == appliance.Id);

                SaveAppliances();
                RefreshDashboard();
            },
            destructive: true);
    }

    // =========================================================
    // OUTAGES
    // =========================================================

    private async void OnLogOutageClicked(
        object sender,
        EventArgs e)
    {
        await ShowOutageModal();
    }

    private async Task ShowOutageModal()
    {
        await OpenModalAsync(
            "outage",
            "Record an outage",
            "Keep a record of the interruption and its duration.",
            "POWER HISTORY");

        AddModalLabel("Outage date");

        _outageDatePicker = new DatePicker
        {
            Date = DateTime.Today,
            MaximumDate = DateTime.Today,
            Format = "dd MMM yyyy",
            BackgroundColor = GetThemeColor(
                Color.FromArgb("#F4F6F8"),
                Color.FromArgb("#252C34")),
            TextColor = GetThemeColor(
                Color.FromArgb("#20262D"),
                Color.FromArgb("#F3F5F7"))
        };

        ModalContent.Children.Add(_outageDatePicker);

        AddModalLabel("Start time");

        _outageTimePicker = new TimePicker
        {
            Time = DateTime.Now.TimeOfDay,
            Format = "hh:mm tt",
            BackgroundColor = GetThemeColor(
                Color.FromArgb("#F4F6F8"),
                Color.FromArgb("#252C34")),
            TextColor = GetThemeColor(
                Color.FromArgb("#20262D"),
                Color.FromArgb("#F3F5F7"))
        };

        ModalContent.Children.Add(_outageTimePicker);

        AddModalEntry(
            "duration",
            "Duration (minutes)",
            "",
            "e.g. 120",
            Keyboard.Numeric);

        AddModalEditor(
            "notes",
            "",
            "Optional notes about the outage");

        AddModalButton(
            "Save outage",
            async (_, _) => await SaveOutageModal(),
            primary: true);

        AddCancelButton();
    }

    private async Task SaveOutageModal()
    {
        if (_isSaving)
            return;

        if (!TryReadInt("duration", out int duration) ||
            duration < 1 ||
            duration > 525600)
        {
            await DisplayAlert(
                "Invalid duration",
                "Enter a duration of at least one minute.",
                "OK");

            return;
        }

        _isSaving = true;

        var notesEditor = ModalContent.Children
            .OfType<Editor>()
            .FirstOrDefault();

        _outages.Add(new OutageRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Date = _outageDatePicker?.Date ?? DateTime.Today,
            StartTime = _outageTimePicker?.Time
                        ?? DateTime.Now.TimeOfDay,
            DurationMinutes = duration,
            Notes = notesEditor?.Text?.Trim() ?? "",
            RecordedAt = DateTime.Now
        });

        SaveOutages();

        await CloseModalAsync();

        RefreshDashboard();
    }

    private async Task ConfirmDeleteOutage(
        OutageRecord outage)
    {
        await ShowConfirmation(
            "Delete this outage record?",
            "The selected outage will be permanently removed from your local history.",
            async () =>
            {
                _outages.RemoveAll(
                    x => x.Id == outage.Id);

                SaveOutages();
                RefreshDashboard();
            },
            destructive: true);
    }

    // =========================================================
    // BACKUP CONFIGURATION
    // =========================================================

    private async void OnConfigureBackupClicked(
        object sender,
        EventArgs e)
    {
        await ShowBackupModal();
    }

    private async Task ShowBackupModal()
    {
        await OpenModalAsync(
            "backup",
            "Backup configuration",
            "Enter your battery specifications and expected load.",
            "BACKUP PLANNER");

        AddModalEntry(
            "voltage",
            "Battery voltage (V)",
            _backup?.BatteryVoltage.ToString(
                CultureInfo.InvariantCulture) ?? "12",
            "e.g. 12",
            Keyboard.Numeric);
            
            AddModalEntry(
"inverter_rating",
"Inverter rated output (watts)",
(_backup?.InverterRatedWatts ?? 1000).ToString(
CultureInfo.InvariantCulture),
"e.g. 1000",
Keyboard.Numeric);

AddModalLabel(
"Enter the inverter's maximum continuous output rating, not its short-duration peak rating.",
muted: true);

        AddModalEntry(
            "capacity",
            "Capacity per battery (Ah)",
            _backup?.CapacityAh.ToString(
                CultureInfo.InvariantCulture) ?? "100",
            "e.g. 100",
            Keyboard.Numeric);

        AddModalEntry(
            "battery_count",
            "Number of batteries",
            _backup?.BatteryCount.ToString(
                CultureInfo.InvariantCulture) ?? "1",
            "e.g. 2",
            Keyboard.Numeric);

        AddModalEntry(
            "usable",
            "Usable capacity (%)",
            _backup?.UsableCapacityPercent.ToString(
                CultureInfo.InvariantCulture) ?? "80",
            "e.g. 80",
            Keyboard.Numeric);

        AddModalEntry(
            "efficiency",
            "Inverter efficiency (%)",
            _backup?.InverterEfficiencyPercent.ToString(
                CultureInfo.InvariantCulture) ?? "90",
            "e.g. 90",
            Keyboard.Numeric);

        AddModalEntry(
            "load",
            "Connected load (watts)",
            _backup?.LoadWatts.ToString(
                CultureInfo.InvariantCulture) ?? "300",
            "e.g. 300",
            Keyboard.Numeric);

        AddModalLabel(
            "Use your actual battery specifications when available.",
            muted: true);

        AddModalButton(
            "Calculate and save",
            async (_, _) => await SaveBackupModal(),
            primary: true);

        AddCancelButton();
    }

    private async Task SaveBackupModal()
    {
        if (_isSaving)
            return;

        if (!TryReadDouble("voltage", out double voltage) ||
            voltage <= 0)
        {
            await DisplayAlert(
                "Invalid voltage",
                "Enter a voltage greater than zero.",
                "OK");

            return;
        }

        if (!TryReadDouble("capacity", out double capacity) ||
            capacity <= 0)
        {
            await DisplayAlert(
                "Invalid capacity",
                "Enter a battery capacity greater than zero.",
                "OK");

            return;
        }

        if (!TryReadInt("battery_count", out int batteryCount) ||
            batteryCount < 1 ||
            batteryCount > 1000)
        {
            await DisplayAlert(
                "Invalid battery count",
                "Enter at least one battery.",
                "OK");

            return;
        }

        if (!TryReadDouble("usable", out double usable) ||
            usable <= 0 ||
            usable > 100)
        {
            await DisplayAlert(
                "Invalid usable capacity",
                "Enter a percentage greater than zero and no more than 100.",
                "OK");

            return;
        }

        if (!TryReadDouble("efficiency", out double efficiency) ||
            efficiency <= 0 ||
            efficiency > 100)
        {
            await DisplayAlert(
                "Invalid efficiency",
                "Enter an efficiency percentage greater than zero and no more than 100.",
                "OK");

            return;
        }

        if (!TryReadDouble("load", out double load) ||
            load <= 0)
        {
            await DisplayAlert(
                "Invalid load",
                "Enter a connected load greater than zero watts.",
                "OK");

            return;
        }
        if (!TryReadDouble("inverter_rating", out double inverterRating) ||
inverterRating <= 0)
{
await DisplayAlert(
"Invalid inverter rating",
"Enter a rated inverter output greater than zero watts.",
"OK");

return;

}

        _isSaving = true;

        double runtime = CalculateRuntime(
            voltage,
            capacity,
            batteryCount,
            usable,
            efficiency,
            load);

        _backup = new BackupConfiguration
{
BatteryVoltage = voltage,
CapacityAh = capacity,
BatteryCount = batteryCount,
UsableCapacityPercent = usable,
InverterEfficiencyPercent = efficiency,
LoadWatts = load,
InverterRatedWatts = inverterRating,
EstimatedRuntimeHours = runtime,
UpdatedAt = DateTime.Now
};

        SaveBackup();

        await CloseModalAsync();

        RefreshDashboard();
        RefreshBackupPlanner();
    }

    private async void OnClearBackupClicked(
        object sender,
        EventArgs e)
    {
        if (_backup == null)
        {
            await DisplayAlert(
                "Nothing to clear",
                "You do not have a saved backup configuration.",
                "OK");

            return;
        }

        await ShowConfirmation(
            "Clear backup configuration?",
            "Your saved battery and load settings will be removed.",
            async () =>
            {
                _backup = null;
                SaveBackup();

await CloseModalAsync();

RefreshDashboard();
RefreshBackupPlanner();

await CheckBackupAlertsAsync();
            },
            destructive: true);
    }

    // =========================================================
    // CONFIRMATION MODALS
    // =========================================================

    private async Task ShowConfirmation(
        string title,
        string message,
        Func<Task> confirmAction,
        bool destructive = false)
    {
        await OpenModalAsync(
            "confirmation",
            title,
            message,
            "PLEASE CONFIRM");

        AddModalLabel(
            "This action changes data stored on your device.",
            muted: true);

        AddModalButton(
            destructive ? "Yes, delete / clear" : "Confirm",
            async (_, _) =>
            {
                if (_isSaving)
                    return;

                _isSaving = true;

                try
                {
                    await confirmAction();
                    await CloseModalAsync();
                }
                catch (Exception ex)
                {
                    await CloseModalAsync();

                    await DisplayAlert(
                        "Action failed",
                        ex.Message,
                        "OK");
                }
                finally
                {
                    _isSaving = false;
                }
            },
            destructive: destructive,
            primary: !destructive);

        AddCancelButton();
    }
    
    private void RefreshBudgetCard()
{
    double estimatedSpending = MonthlyCost();

    // No budget has been configured.
    if (_monthlyBudget <= 0)
    {
        BudgetLimitLabel.Text = "Not set";

        BudgetSpentLabel.Text =
            FormatCurrency(estimatedSpending);

        BudgetRemainingLabel.Text = "Not set";

        BudgetProgressBar.Progress = 0;

        BudgetPercentageLabel.Text = "0%";

        BudgetProgressLabel.Text =
            "Set a budget to begin tracking.";

        BudgetStatusLabel.Text =
            "Choose a monthly spending limit to see your " +
            "estimated spending, remaining balance and budget alerts.";

        BudgetProgressBar.ProgressColor =
            Color.FromArgb("#8A939D");

        return;
    }

    // Calculate spending against the configured limit.
    double remaining = _monthlyBudget - estimatedSpending;

    double usageRatio =
        estimatedSpending / _monthlyBudget;

    double progress =
        Math.Clamp(usageRatio, 0.0, 1.0);

    double percentage =
        usageRatio * 100.0;

    BudgetLimitLabel.Text =
        FormatCurrency(_monthlyBudget);

    BudgetSpentLabel.Text =
        FormatCurrency(estimatedSpending);

    BudgetRemainingLabel.Text =
        FormatCurrency(Math.Abs(remaining));

    BudgetProgressBar.Progress = progress;

    BudgetPercentageLabel.Text =
        $"{percentage:N0}%";

    // Display whether the budget has been exceeded.
    if (remaining < 0)
    {
        BudgetRemainingLabel.Text =
            $"{FormatCurrency(Math.Abs(remaining))} over";

        BudgetProgressLabel.Text =
            "Monthly budget exceeded";

        BudgetProgressBar.ProgressColor =
            Color.FromArgb("#D94F4F");

        BudgetStatusLabel.Text =
            $"You are estimated to exceed your monthly budget by " +
            $"{FormatCurrency(Math.Abs(remaining))}. " +
            "Review your highest-consuming appliances.";
    }
    else if (usageRatio >= 0.90)
    {
        BudgetProgressLabel.Text =
            "Approaching your limit";

        BudgetProgressBar.ProgressColor =
            Color.FromArgb("#D94F4F");

        BudgetStatusLabel.Text =
            $"Warning: you have used {percentage:N0}% of your budget. " +
            $"Only {FormatCurrency(remaining)} remains.";
    }
    else if (usageRatio >= 0.75)
    {
        BudgetProgressLabel.Text =
            "Budget getting tight";

        BudgetProgressBar.ProgressColor =
            Color.FromArgb("#D5A32E");

        BudgetStatusLabel.Text =
            $"You have used {percentage:N0}% of your budget. " +
            $"Approximately {FormatCurrency(remaining)} remains. " +
            "Consider reducing unnecessary electricity consumption.";
    }
    else
    {
        BudgetProgressLabel.Text =
            "Within your monthly budget";

        BudgetProgressBar.ProgressColor =
            Color.FromArgb("#9BE564");

        BudgetStatusLabel.Text =
            $"You have used {percentage:N0}% of your budget. " +
            $"{FormatCurrency(remaining)} remains available.";
    }
}
private async Task CheckBudgetAlertsAsync()
{
if (_monthlyBudget <= 0)
return;

double projectedCost = MonthlyCost();

if (double.IsNaN(projectedCost) ||
    double.IsInfinity(projectedCost) ||
    projectedCost < 0)
{
    return;
}

double percentage =
    (projectedCost / _monthlyBudget) * 100;

string cycle = DateTime.Now.ToString("yyyy-MM");
string alertKey = $"{BudgetAlertsKey}_{cycle}";

string savedAlerts = Preferences.Get(
    alertKey,
    "");

var sentThresholds = new HashSet<int>(
    savedAlerts
        .Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries)
        .Select(value =>
            int.TryParse(value, out int result)
                ? result
                : 0));

int[] thresholds = { 75, 90, 100 };

foreach (int threshold in thresholds)
{
    if (percentage < threshold ||
        sentThresholds.Contains(threshold))
    {
        continue;
    }

    var request = new NotificationRequest
    {
        NotificationId = 2000 + threshold,

        Title = threshold switch
        {
            75 => "Electricity budget warning",
            90 => "Electricity budget nearly reached",
            _ => "Electricity budget reached"
        },

        Description = threshold switch
        {
            75 =>
                $"Your projected electricity cost has reached {percentage:F0}% of your monthly budget.",

            90 =>
                $"Your projected electricity cost has reached {percentage:F0}% of your monthly budget. Consider reducing consumption.",

            _ =>
                $"Your projected electricity cost is {percentage:F0}% of your monthly budget. Your budget has been reached or exceeded."
        }
    };

    await LocalNotificationCenter.Current.Show(request);

    sentThresholds.Add(threshold);

    Preferences.Set(
        alertKey,
        string.Join(
            ",",
            sentThresholds
                .Where(value => value > 0)
                .OrderBy(value => value)));
}

}
private async void OnSetBudgetClicked(
    object sender,
    EventArgs e)
{
    string? input = await DisplayPromptAsync(
        "Monthly electricity budget",
        "Enter your maximum electricity spending for one month in naira.",
        accept: "Save budget",
        cancel: "Cancel",
        placeholder: "e.g. 25000",
        initialValue: _monthlyBudget > 0
            ? _monthlyBudget.ToString("0.##", CultureInfo.InvariantCulture)
            : string.Empty,
        maxLength: 12,
        keyboard: Keyboard.Numeric);

    if (input == null)
        return;

    input = input.Trim();

    if (!double.TryParse(
            input,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out double newBudget)
        || double.IsNaN(newBudget)
        || double.IsInfinity(newBudget)
        || newBudget <= 0)
    {
        await DisplayAlert(
            "Invalid budget",
            "Enter a valid amount greater than ₦0.",
            "OK");

        return;
    }

    _monthlyBudget = newBudget;

    SaveBudget();

    RefreshBudgetCard();
    await CheckBudgetAlertsAsync();

    await DisplayAlert(
        "Budget saved",
        $"Your monthly electricity budget is " +
        $"{FormatCurrency(_monthlyBudget)}.",
        "OK");
}

    // =========================================================
    // MODELS
    // =========================================================

    public class HouseholdSetup
    {
        public string Location { get; set; } = "";
        public double TariffPerKwh { get; set; }
        public string SupplyType { get; set; } = "Grid";
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public class ApplianceRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public double Watts { get; set; }
        public double HoursPerDay { get; set; }
        public int Quantity { get; set; } = 1;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public class OutageRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Date { get; set; } = DateTime.Today;
        public TimeSpan StartTime { get; set; }
        public int DurationMinutes { get; set; }
        public string Notes { get; set; } = "";
        public DateTime RecordedAt { get; set; } = DateTime.Now;
    }

    public class BackupConfiguration
{
public double BatteryVoltage { get; set; }

public double CapacityAh { get; set; }

public int BatteryCount { get; set; } = 1;

public double UsableCapacityPercent { get; set; } = 80;

public double InverterEfficiencyPercent { get; set; } = 90;

public double LoadWatts { get; set; }

// Maximum continuous AC output rating of the inverter.
// The default preserves compatibility with older saved configurations.
public double InverterRatedWatts { get; set; } = 1000;

public double EstimatedRuntimeHours { get; set; }

public DateTime UpdatedAt { get; set; } = DateTime.Now;

}
    public class MonthlySnapshot
{
    public string MonthKey { get; set; } = "";
    public DateTime Month { get; set; }
    public double EstimatedConsumptionKwh { get; set; }
    public double EstimatedCost { get; set; }
    public int ApplianceCount { get; set; }
    public int OutageCount { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

}