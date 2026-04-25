namespace AgroShield.Application.DTOs.Dashboard;

public record DashboardStatsDto(
    int TotalFarms,
    int ActiveAnomalies,
    decimal SuspiciousAmount,
    int SickPlantsToday,
    double AverageRiskScore);
