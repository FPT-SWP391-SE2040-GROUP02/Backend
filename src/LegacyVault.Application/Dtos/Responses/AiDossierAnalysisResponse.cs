namespace LegacyVault.Application.Dtos.Responses;

public sealed class AiDossierChecklistItem
{
    public string Category { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // VALID, WARNING, ANOMALY, INVALID
    public string Detail { get; set; } = string.Empty;
}

public sealed class AiDossierAnalysisResponse
{
    public Guid CaseId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = "LOW"; // LOW, MEDIUM, HIGH
    public string RiskReason { get; set; } = string.Empty;
    public string Recommendation { get; set; } = "APPROVE"; // APPROVE, SUPPLEMENT, REJECT
    public string RecommendationReason { get; set; } = string.Empty;
    public List<AiDossierChecklistItem> Checklist { get; set; } = new();
    public string ProposedDecisionNote { get; set; } = string.Empty;
    public DateTimeOffset AnalyzedAt { get; set; }
    public string ModelName { get; set; } = "gemini-flash-lite-latest";
}
