namespace Reviews.Application;

/// <summary>Абстракція бізнес-метрик відгуків; реалізація — Reviews.WebApi/Metrics/ReviewMetrics.</summary>
public interface IReviewMetrics
{
    void ReviewCreated(string type, int rating);
    void RecordDuration(string operation, bool success, double elapsedMs);
}
