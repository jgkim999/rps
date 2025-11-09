namespace Rps.Share.MessageBroker;

/// <summary>
/// 메시지 큐 발송자 타입을 정의하는 열거형
/// </summary>
public enum MqSenderType
{
    /// <summary>
    /// 다중 수신자 모드 - 팬아웃 방식으로 모든 컨슈머에게 메시지 전송
    /// </summary>
    Multi,

    /// <summary>
    /// 고유 수신자 모드 - 특정 대상에게만 메시지 전송
    /// </summary>
    Unique,

    /// <summary>
    /// 임의 수신자 모드 - 라운드 로빈 방식으로 사용 가능한 컨슈머 중 하나에게 메시지 전송
    /// </summary>
    Any,
}
