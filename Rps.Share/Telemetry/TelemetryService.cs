using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Rps.Share.Telemetry;

/// <summary>
/// 사용자 정의 텔레메트리 서비스
/// </summary>
public sealed class TelemetryService : ITelemetryService, IDisposable
{
    private readonly ILogger<TelemetryService> _logger;
    
    /// <summary>
    /// ActivitySource
    /// </summary>
    private readonly ActivitySource _activitySource;

    /// <summary>
    /// 애플리케이션의 _meter
    /// </summary>
    private readonly Meter _meter;

    // 사용자 정의 메트릭 정의
    private readonly ConcurrentDictionary<string, Counter<long>> _counters = new();
    private readonly ConcurrentDictionary<string, Histogram<double>> _histograms = new();
    private readonly ConcurrentDictionary<string, Gauge<double>> _gauges = new();

    string ITelemetryService.ActiveSourceName => _activitySource.Name;
    string ITelemetryService.MeterName => _meter.Name;
    
    /// <summary>
    /// TelemetryService 생성자
    /// </summary>
    public TelemetryService(string serviceName, string serviceVersion, ILogger<TelemetryService> logger)
    {
        _logger = logger;
        _activitySource = new ActivitySource(serviceName, serviceVersion);
        _meter = new Meter(serviceName, serviceVersion);
    }

    /// <summary>
    /// 사용자 정의 Activity를 시작합니다.
    /// </summary>
    /// <param name="operationName">작업 이름</param>
    /// <param name="tags">추가할 태그</param>
    /// <returns>시작된 Activity</returns>
    public Activity? StartActivity(string operationName, Dictionary<string, object?>? tags)
    {
        var activity = _activitySource.StartActivity(operationName);

        if (activity != null && tags != null)
        {
            foreach (var tag in tags)
            {
                activity.SetTag(tag.Key, tag.Value);
            }
        }

        return activity;
    }

    public Activity? StartActivity(string operationName, ActivityKind kind, Dictionary<string, object?>? tags = null)
    {
        var activity = _activitySource.StartActivity(operationName, kind);

        if (activity != null && tags != null)
        {
            foreach (var tag in tags)
            {
                activity.SetTag(tag.Key, tag.Value);
            }
        }

        return activity;
    }

    public Activity? StartActivity(string operationName, ActivityKind kind, ActivityContext? parentContext, Dictionary<string, object?>? tags)
    {
        if (parentContext is null)
            return null;
        var span = _activitySource.StartActivity(operationName, kind, parentContext.Value);
        if (span is null)
            return null;
        if (tags != null)
        {
            foreach (var tag in tags)
            {
                span.SetTag(tag.Key, tag.Value);
            }
        }
        return span;
    }

    public Activity? StartActivity(string operationName, ActivityKind kind, string? parentTraceId)
    {
        if (parentTraceId is null)
            return null;
        var span = _activitySource.StartActivity(operationName, kind, parentTraceId);
        return span;
    }

    /// <summary>
    /// 부모 없이 새로운 root Activity를 시작합니다.
    /// Activity.Current를 임시로 null로 설정하여 기존 trace와의 연결을 끊습니다.
    /// </summary>
    public Activity? StartRootActivity(string operationName, ActivityKind kind, Dictionary<string, object?>? tags = null)
    {
        // 기존 Activity.Current를 저장
        var previousActivity = Activity.Current;

        try
        {
            // Activity.Current를 null로 설정하여 부모 연결 끊기
            Activity.Current = null;

            // 새로운 root activity 시작
            var activity = _activitySource.StartActivity(operationName, kind);

            if (activity != null && tags != null)
            {
                foreach (var tag in tags)
                {
                    activity.SetTag(tag.Key, tag.Value);
                }
            }

            return activity;
        }
        finally
        {
            // Activity.Current는 새로 생성된 activity로 자동 설정되므로
            // 이전 activity로 복원하지 않음
        }
    }

    /// <summary>
    /// 비즈니스 메트릭을 기록합니다.
    /// </summary>
    /// <param name="metricName">메트릭 이름</param>
    /// <param name="unit">"1" "s" "m"</param>
    /// <param name="value">값</param>
    /// <param name="tags">태그</param>
    public void AddCounter(string metricName, string unit, long value, Dictionary<string, object?>? tags)
    {
        var counter = _counters.GetOrAdd(metricName, m =>
            _meter.CreateCounter<long>(
            name: $"business_{m}",
            unit: unit,
            description: $"Business metric: {m}"));

        var tagList = new TagList();
        if (tags != null)
        {
            foreach (var tag in tags)
            {
                tagList.Add(tag.Key, tag.Value?.ToString());
            }
        }

        counter.Add(value, tagList);
    }
    
    public void AddHistogram(string metricName, string unit, double value, Dictionary<string, object?>? tags)
    {
        var histogram = _histograms.GetOrAdd(metricName, m =>
            _meter.CreateHistogram<double>(
            name: $"business_{m}",
            unit: unit,
            description: $"Business metric: {m}"));

        var tagList = new TagList();
        if (tags != null)
        {
            foreach (var tag in tags)
            {
                tagList.Add(tag.Key, tag.Value?.ToString());
            }
        }

        histogram.Record(value, tagList);
    }
    
    public void AddGauge(string metricName, string unit, double value, Dictionary<string, object?>? tags)
    {
        var gauge = _gauges.GetOrAdd(metricName, m =>
            _meter.CreateGauge<double>(
            name: $"business_{m}",
            unit: unit,
            description: $"Business metric: {m}"));

        var tagList = new TagList();
        if (tags != null)
        {
            foreach (var tag in tags)
            {
                tagList.Add(tag.Key, tag.Value?.ToString());
            }
        }

        gauge.Record(value, tagList);
    }

    /// <summary>
    /// Activity에 에러 정보를 설정합니다.
    /// </summary>
    /// <param name="activity">Activity 객체</param>
    /// <param name="exception">예외 객체</param>
    public void SetActivityError(Activity? activity, Exception exception)
    {
        if (activity == null)
            return;

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag("error", true);
        activity.SetTag("error.type", exception.GetType().Name);
        activity.SetTag("error.message", exception.Message);
        activity.SetTag("error.stack_trace", exception.StackTrace);

        // 예외를 Activity 이벤트로 기록
        activity.AddEvent(new ActivityEvent("exception", DateTimeOffset.UtcNow, new ActivityTagsCollection
        {
            ["exception.type"] = exception.GetType().FullName,
            ["exception.message"] = exception.Message,
            ["exception.stacktrace"] = exception.StackTrace
        }));
    }

    /// <summary>
    /// Activity에 성공 상태를 설정합니다.
    /// </summary>
    /// <param name="activity">Activity 객체</param>
    /// <param name="message">성공 메시지</param>
    public void SetActivitySuccess(Activity? activity, string? message)
    {
        if (activity == null)
            return;

        activity.SetStatus(ActivityStatusCode.Ok, message ?? "Operation completed successfully");
        activity.SetTag("success", true);
    }

    /// <summary>
    /// 리소스 정리
    /// </summary>
    public void Dispose()
    {
        _activitySource.Dispose();
        _meter.Dispose();
    }
}