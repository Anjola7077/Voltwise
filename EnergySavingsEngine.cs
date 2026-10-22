using System;
using System.Collections.Generic;
using System.Linq;

namespace Voltwise;

public sealed class EnergySavingRecommendation
{
public string Title { get; init; } = "";
public string Description { get; init; } = "";
public string Priority { get; init; } = "Normal";
public double EstimatedMonthlySavings { get; init; }
public string Category { get; init; } = "Energy";
}

public static class EnergySavingsEngine
{
public static List<EnergySavingRecommendation> Analyse(
IEnumerable<(string Name, double Watts, double HoursPerDay)> appliances,
double tariffPerKwh,
double monthlyBudget,
double monthlyConsumptionKwh,
double estimatedMonthlyCost,
double backupRuntimeHours)
{
var recommendations =
new List<EnergySavingRecommendation>();

    var validAppliances = appliances
        .Where(a =>
            !string.IsNullOrWhiteSpace(a.Name) &&
            double.IsFinite(a.Watts) &&
            double.IsFinite(a.HoursPerDay) &&
            a.Watts > 0 &&
            a.HoursPerDay > 0)
        .ToList();

    if (validAppliances.Count == 0)
    {
        recommendations.Add(new EnergySavingRecommendation
        {
            Title = "Start tracking appliances",
            Description =
                "Add your appliances, rated wattage and average daily usage to unlock personalized savings recommendations.",
            Priority = "High",
            Category = "Getting started"
        });

        return recommendations;
    }

    var ranked = validAppliances
        .Select(a => new
        {
            a.Name,
            a.Watts,
            a.HoursPerDay,
            MonthlyKwh = a.Watts * a.HoursPerDay * 30 / 1000.0
        })
        .OrderByDescending(a => a.MonthlyKwh)
        .ToList();

    var largest = ranked.First();

    if (tariffPerKwh > 0)
{
foreach (var appliance in ranked.Take(5))
{
double reducibleHours =
Math.Min(1, appliance.HoursPerDay);

    double savings =
        appliance.Watts *
        reducibleHours *
        30.0 / 1000.0 *
        tariffPerKwh;

    if (savings <= 0)
        continue;

    recommendations.Add(new EnergySavingRecommendation
    {
        Title = $"Optimize {appliance.Name}",
        Description =
            $"This appliance uses approximately " +
            $"{appliance.MonthlyKwh:N2} kWh per month. " +
            $"Reducing its daily usage by up to " +
            $"{reducibleHours:N1} hour(s) could save " +
            $"approximately {savings:N2} per month. " +
            "Only reduce usage when practical and safe.",
        Priority = appliance.MonthlyKwh >=
                   ranked.First().MonthlyKwh * 0.5
            ? "High"
            : "Normal",
        EstimatedMonthlySavings = savings,
        Category = "Usage optimization"
    });
}

}
else
{
recommendations.Add(new EnergySavingRecommendation
{
Title = "Set your electricity tariff",
Description =
"Enter your cost per kWh to calculate estimated savings " +
"for individual appliances.",
Priority = "High",
Category = "Budget"
});
}

    if (monthlyBudget > 0 && estimatedMonthlyCost > 0)
    {
        double percentage =
            estimatedMonthlyCost / monthlyBudget * 100;

        if (percentage >= 100)
        {
            recommendations.Add(new EnergySavingRecommendation
            {
                Title = "Estimated budget exceeded",
                Description =
                    $"Your estimated monthly cost is {percentage:N0}% of your budget. Review high-consumption appliances and reduce nonessential usage where practical.",
                Priority = "High",
                Category = "Budget"
            });
        }
        else if (percentage >= 80)
        {
            recommendations.Add(new EnergySavingRecommendation
            {
                Title = "Protect your remaining budget",
                Description =
                    $"Your estimated monthly cost has reached {percentage:N0}% of your budget. Review discretionary appliance usage for the rest of the month.",
                Priority = "High",
                Category = "Budget"
            });
        }
        else
        {
            recommendations.Add(new EnergySavingRecommendation
            {
                Title = "Budget currently within range",
                Description =
                    $"Your estimated monthly cost is {percentage:N0}% of your set budget. Continue monitoring consumption.",
                Priority = "Normal",
                Category = "Budget"
            });
        }
    }

    if (backupRuntimeHours >= 0 &&
        backupRuntimeHours < 0.5)
    {
        recommendations.Add(new EnergySavingRecommendation
        {
            Title = "Extend backup runtime",
            Description =
                "Your estimated backup runtime is below 30 minutes. Consider switching off nonessential loads and checking your battery charge and inverter limits.",
            Priority = "High",
            Category = "Backup power"
        });
    }
    else if (backupRuntimeHours >= 0 &&
             backupRuntimeHours < 1)
    {
        recommendations.Add(new EnergySavingRecommendation
        {
            Title = "Conserve backup power",
            Description =
                "Your estimated backup runtime is below one hour. Prioritize essential appliances during an outage.",
            Priority = "Normal",
            Category = "Backup power"
        });
    }

    if (monthlyConsumptionKwh > 0)
    {
        recommendations.Add(new EnergySavingRecommendation
        {
            Title = "Track your monthly consumption",
            Description =
                $"Your current appliance-based estimate is {monthlyConsumptionKwh:N2} kWh per month. Compare it with future months to identify changes.",
            Priority = "Normal",
            Category = "Consumption"
        });
    }

    return recommendations
        .OrderBy(r => r.Priority == "High" ? 0 : 1)
        .ThenByDescending(r => r.EstimatedMonthlySavings)
        .ToList();
}

}