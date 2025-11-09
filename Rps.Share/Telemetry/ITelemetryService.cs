using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Rps.Share.Telemetry;

public interface ITelemetryService
{
    string ActiveSourceName { get; }
    string MeterName { get; }

    /// <summary>
    /// 사용자 정의 Activity를 시작합니다.
    /// </summary>
    /// <param name="operationName">작업 이름</param>
    /// <param name="tags">추가할 태그</param>
    /// <returns>The started <see cref="Activity"/> instance, or null if the activity could not be started.</returns>
    Activity? StartActivity(string operationName, Dictionary<string, object?>? tags = null);
    Activity? StartActivity(string operationName, ActivityKind kind, Dictionary<string, object?>? tags = null);
    Activity? StartActivity(string operationName, ActivityKind kind, ActivityContext? parentContext,
        Dictionary<string, object?>? tags = null);
    Activity? StartActivity(string operationName, ActivityKind kind, string? parentTraceId);

    /// <summary>
    /// 부모 없이 새로운 root Activity를 시작합니다. 기존 trace와 연결되지 않습니다.
    /// </summary>
    /// <param name="operationName">작업 이름</param>
    /// <param name="kind">Activity 종류</param>
    /// <param name="tags">추가할 태그</param>
    /// <returns>The started root <see cref="Activity"/> instance, or null if the activity could not be started.</returns>
    Activity? StartRootActivity(string operationName, ActivityKind kind, Dictionary<string, object?>? tags = null);

    void SetActivityError(Activity? activity, Exception exception);

    void SetActivitySuccess(Activity? activity, string? message);
    
    /// <summary>
    /// 비즈니스 메트릭을 기록합니다.
    /// </summary>
    /// <param name="metricName">메트릭 이름</param>
    /// <param name="unit">"1" "s" "m"</param>
    /// <param name="value">값</param>
    /// <param name="tags">태그</param>
    void AddCounter(string metricName, string unit, long value, Dictionary<string, object?>? tags);

    void AddHistogram(string metricName, string unit, double value, Dictionary<string, object?>? tags);

    void AddGauge(string metricName, string unit, double value, Dictionary<string, object?>? tags);
}
