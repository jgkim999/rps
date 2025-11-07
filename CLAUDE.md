# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 프로젝트 개요

ASP.NET Core 9.0 기반의 가위바위보(RPS) 멀티플레이어 게임입니다. SignalR을 통한 실시간 통신, Redis를 통한 분산 상태 관리, Blazor Server를 통한 UI를 제공합니다. Redis 백플레인을 지원하여 다중 서버 배포가 가능하도록 설계되었습니다.

## 개발 명령어

### 빌드 및 실행
```bash
# 프로젝트 빌드
dotnet build Rps.sln

# 애플리케이션 실행
dotnet run --project Rps/Rps.csproj

# 특정 환경으로 실행
ASPNETCORE_ENVIRONMENT=Development dotnet run --project Rps/Rps.csproj
```

### 접속 URL
- **로컬**: `http://localhost:5184/login`
- **네트워크**: `http://[서버IP]:5184/login` (예: http://192.168.0.10:5184/login)
- 애플리케이션은 `0.0.0.0:5184`에서 리스닝하여 모든 네트워크 인터페이스에서 접속 가능

### 사전 요구사항
- .NET 9.0 SDK
- Redis 서버 (기본 설정: localhost:6379)
- `Rps/appsettings.json` 또는 환경별 설정 파일에서 Redis 엔드포인트 설정 필요
- 방화벽에서 포트 5184 허용 (외부 접속 시)

## 아키텍처

### 다층 Redis 통합

이 애플리케이션은 Redis를 세 가지 목적으로 사용하며, 각각 `appsettings.json`에서 별도의 연결 문자열이 필요합니다:

1. **SignalR 백플레인** (`SignalRBackplane`): Redis를 통해 메시지를 브로드캐스트하여 여러 서버 인스턴스에서 SignalR이 작동하도록 함
2. **FusionCache 분산 캐시** (`FusionCacheRedisCache`): 분산 무효화 기능이 있는 캐시 데이터 저장
3. **FusionCache 백플레인** (`FusionCacheBackplane`): 여러 서버 인스턴스 간 캐시 무효화 동기화

모든 Redis 작업은 현재 환경 이름(Development/Production)을 접두사로 사용하여 환경 간 데이터를 격리합니다.

### RedisManager 패턴

`RedisManager` 클래스 (Rps/RedisManager.cs)는 `ConnectionMultiplexer`를 래핑하고 모든 키에 환경 이름을 자동으로 접두사로 붙입니다. `RedisManager.GetDatabase()`를 통해 Redis에 액세스하면 키가 자동으로 네임스페이스화됩니다.

**중요**: `RedisManager`를 통한 `IDatabase` 메서드 직접 사용 시 환경 접두사가 자동으로 붙습니다. 사용자 데이터 키 패턴: `{environment}:user:{userId}` 또는 `{environment}:user:nickname:{nickname}`.

### Blazor Components 아키텍처

이 프로젝트는 Razor Pages와 Blazor Components를 혼합하여 사용합니다:

**프로젝트 구조**:
- `Components/App.razor`: Blazor 앱의 루트 컴포넌트
- `Components/Routes.razor`: Blazor 라우터 설정 (404 처리 포함)
- `Components/_Imports.razor`: 모든 Blazor 컴포넌트에서 사용할 공통 using 지시문
- `Components/Pages/`: Blazor 페이지 컴포넌트들 (Login.razor, Lobby.razor)
- `Pages/`: 기존 Razor Pages (.cshtml 파일들)

**렌더 모드**:
- 모든 Blazor 페이지는 `@rendermode InteractiveServer`를 사용하여 SignalR 기반 실시간 인터랙티브 모드로 작동
- `Components/_Imports.razor`에 `@using static Microsoft.AspNetCore.Components.Web.RenderMode` 추가로 렌더 모드 사용 가능

**Program.cs 설정**:
```csharp
// 외부 접속 허용
builder.WebHost.UseUrls("http://0.0.0.0:5184");

// 서비스 등록
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// 미들웨어 파이프라인 (순서 중요!)
app.UseRouting();
app.UseAuthorization();
app.UseAntiforgery();  // Blazor Interactive Server 필수

// 엔드포인트 매핑
app.MapRazorComponents<Rps.Components.App>()
    .AddInteractiveServerRenderMode();
```

**중요 사항**:
- 새로운 Blazor 페이지 추가 시 `Components/Pages/` 디렉터리에 생성하고 `@rendermode InteractiveServer` 디렉티브를 추가해야 합니다
- `app.UseAntiforgery()`는 반드시 `app.UseAuthorization()` 이후, 엔드포인트 매핑 이전에 호출되어야 합니다
- Anti-forgery 미들웨어는 CSRF 공격을 방지하기 위해 필수입니다

**JavaScript 통합**:
`Components/App.razor`에서 필요한 JavaScript 파일들을 로드합니다:
```html
<script src="_framework/blazor.web.js"></script>
<script src="~/js/signalr/dist/browser/signalr.min.js"></script>
<script src="~/js/login.js"></script>
<script src="~/js/lobby.js"></script>
```

로드 순서가 중요합니다:
1. Blazor 프레임워크 (blazor.web.js)
2. SignalR 클라이언트 라이브러리 (login.js와 lobby.js가 의존)
3. 페이지별 JavaScript 파일들

**JavaScript 로딩 타이밍 문제 해결**:
Blazor 컴포넌트의 `OnAfterRenderAsync`가 JavaScript 파일 로드보다 먼저 실행될 수 있어 간헐적으로 "function not found" 오류가 발생할 수 있습니다. 이를 해결하기 위해 재시도 로직을 구현했습니다:

```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        objRef = DotNetObjectReference.Create(this);

        // 최대 10회, 100ms 간격으로 재시도
        var maxRetries = 10;
        var retryCount = 0;

        while (retryCount < maxRetries)
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("setLoginComponentReference", objRef);
                break; // 성공 시 종료
            }
            catch (JSException)
            {
                retryCount++;
                if (retryCount >= maxRetries)
                {
                    // 최종 실패 시 사용자에게 안내
                    statusMessage = "JavaScript 로딩 실패. 페이지를 새로고침하세요.";
                    StateHasChanged();
                    throw;
                }
                await Task.Delay(100);
            }
        }
    }
}
```

이 패턴은 Login.razor:63-94 및 Lobby.razor:133-164에 구현되어 있습니다.

### Blazor-JavaScript 상호작용

**DotNetObjectReference 패턴**:
```csharp
// Blazor 컴포넌트에서
objRef = DotNetObjectReference.Create(this);
await JSRuntime.InvokeVoidAsync("setLoginComponentReference", objRef);

// JavaScript에서 Blazor 메서드 호출
componentRef.invokeMethodAsync('HandleLoginSuccess', userId, nickname, statistics);
```

**JSInvokable 메서드**:
JavaScript에서 호출할 Blazor 메서드는 `[JSInvokable]` 어트리뷰트 필요:
```csharp
[JSInvokable]
public void HandleLoginSuccess(long userId, string userNickname, object statistics)
{
    // JavaScript에서 호출 가능
}
```

**중요**: `DotNetObjectReference`는 반드시 `IDisposable.Dispose()`로 정리해야 메모리 누수 방지

### SignalR Hub 아키텍처

`GameHub` (Rps/Hubs/GameHub.cs:136)는 `/gamehub`에 매핑된 메인 실시간 통신 허브입니다. 주요 패턴:

- **연결 생명주기**: OnConnectedAsync/OnDisconnectedAsync로 클라이언트 연결 처리
  - FusionCache와 Redis Set 모두에 연결 상태 저장
  - 브로드캐스트를 위해 클라이언트를 "SignalR Users" 그룹에 추가
  - 중요하지 않은 작업은 실패해도 계속 진행하는 우아한 에러 처리

- **에러 처리 패턴**: 모든 허브 메서드는 일관된 패턴을 따릅니다:
  1. 입력 매개변수 검증
  2. `OnError` 또는 메서드별 에러 콜백을 통해 호출자에게 구체적인 에러 메시지 전송
  3. 구조화된 로깅(Serilog)으로 에러 기록
  4. 가능한 경우 실행 계속 (예: 캐시 실패가 사용자 작업을 중단하지 않음)

- **클라이언트 통신**: 강타입 콜백 메서드 사용:
  - `OnLoginSuccess`, `OnLoginFailed`
  - `OnSkinSelected`, `OnSkinSelectionFailed`
  - `ReceiveMessage`, `OnError`

### User Service 계층

`IUserService` (Rps/Services/IUserService.cs)는 Redis를 영구 저장소로 사용하여 모든 사용자 데이터 작업을 추상화합니다:

- 사용자 프로필은 Redis Hash로 `user:{userId}` 키에 저장
- 닉네임-사용자ID 매핑은 String으로 `user:nickname:{nickname}` 키에 저장
- 사용자 ID 카운터는 `user:id:counter` 키로 관리
- 모든 예외는 포착, 로깅되고 클라이언트를 위한 한글 에러 메시지와 함께 `ArgumentException` 또는 `InvalidOperationException`으로 재발생

### 데이터 모델

`Rps/Models/` 위치:
- `UserProfile`: 통계 및 선택된 스킨을 포함한 완전한 사용자 데이터
- `UserStatistics`: 계산된 속성(TotalGames, WinRate)이 있는 게임 통계
- `GameResult`: Win/Loss/Draw 열거형
- `HandShape`: Rock/Paper/Scissors 열거형

### 설정 시스템

환경별 설정 우선순위:
1. `appsettings.json` (기본 설정)
2. `appsettings.{Environment}.json` (환경별 재정의)
3. 환경 변수 (최우선)

`ASPNETCORE_ENVIRONMENT` 환경 변수가 내장 환경 감지를 재정의합니다.

### Serilog 로깅

`appsettings.json`에서 여러 싱크로 구성:
- 트레이스 컨텍스트(TraceId, SpanId)가 있는 콘솔 출력
- 일일 로테이션이 있는 파일 로깅 (7일 보관, 10MB 크기 제한)
- 분산 추적을 위한 OpenTelemetry 싱크 (appsettings에서 엔드포인트 설정)

로그는 `logs/rps-.log`에 일별로 저장됩니다.

## 공통 패턴

### 의존성 주입
- `IUserService`는 scoped로 등록
- `RedisManager`는 환경 접두사와 함께 singleton으로 등록
- `IFusionCache`는 FusionCache 빌더에 의해 자동 등록
- 허브 메서드는 `IServiceProvider`를 사용하여 scoped 서비스 액세스를 위한 스코프 생성

### 에러 메시지
모든 사용자 대상 에러 메시지는 한글로 작성됩니다. 새로운 검증 추가 시 이 패턴을 따르세요:
- 구조화된 매개변수로 영어로 로깅
- 클라이언트에 한글 에러 메시지 반환
- 구체적인 에러 콜백 사용 (일반적인 예외 사용 금지)

### Redis 키 명명 규칙
기존 패턴을 따르세요:
- 사용자 데이터: `user:{userId}` (hash), `user:nickname:{nickname}` (string)
- 연결된 클라이언트: `ConnectedClient-{connectionId}` (FusionCache)
- 사용자 집합: `Users` (Redis Set)
- 모든 키는 RedisManager에 의해 환경 이름이 자동으로 접두사로 붙음

## 테스트 참고사항

로컬 테스트 시:
- Redis가 localhost:6379에서 실행 중인지 확인
- 디버깅을 위해 `logs/rps-.log`에서 로그 확인
- FusionCache 디버그 로깅은 기본적으로 활성화됨
- SignalR 연결은 브라우저 DevTools를 통해 테스트 가능 (허브는 `/gamehub`에 위치)

## 중요한 구현 세부사항

1. **허브의 Scoped 서비스**: SignalR 허브는 transient이지만, `IUserService`는 scoped입니다. 허브 메서드에서 `IUserService` 액세스 시 항상 스코프를 생성하세요 (패턴은 GameHub.cs:185 참조).

2. **Connection ID 추적**: 각 SignalR 연결은 고유한 `Context.ConnectionId`를 가지며, 이는 사용자 프로필에 저장되고 활성 연결 추적에 사용됩니다.

3. **캐시 vs 영구 저장소**: FusionCache는 임시 데이터(연결된 클라이언트)에 사용되고, Redis 네이티브 작업은 영구 사용자 데이터에 사용됩니다.

4. **CORS 정책**: 개발을 위해 "AllowAll" 정책이 활성화되어 있습니다. 프로덕션 배포 전에 검토하세요.

5. **외부 접속**: `0.0.0.0:5184`로 바인딩하여 모든 네트워크 인터페이스에서 접속 가능합니다. 프로덕션에서는 HTTPS를 사용하고 방화벽 규칙을 강화하세요.

6. **Anti-forgery**: Blazor Interactive Server 모드는 anti-forgery 토큰을 자동으로 관리합니다. `UseAntiforgery()` 미들웨어가 반드시 필요합니다.

7. **JavaScript 통합**: `App.razor`에서 JavaScript 파일들을 올바른 순서로 로드해야 합니다. SignalR 클라이언트 라이브러리는 login.js와 lobby.js보다 먼저 로드되어야 합니다.

8. **JavaScript 로딩 타이밍**: Blazor 컴포넌트의 `OnAfterRenderAsync`가 JavaScript 파일 로드보다 먼저 실행될 수 있습니다. 이를 방지하기 위해 JavaScript 함수 호출 시 재시도 로직(최대 10회, 100ms 간격)을 구현했습니다. 새로운 Blazor 컴포넌트에서 JavaScript 함수를 호출할 때 이 패턴을 따르세요 (Login.razor:63-94 참조).