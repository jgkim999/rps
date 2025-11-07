# 설계 문서

## 개요

이 문서는 SignalR과 Redis를 활용한 Blazor 기반 로그인 및 로비 시스템의 기술적 설계를 정의합니다. 시스템은 Razor Pages 프로젝트 내에서 Blazor 컴포넌트를 사용하여 구현되며, 사용자 인증, 프로필 관리, 게임 통계 조회, 스킨 선택 기능을 제공합니다.

## 아키텍처

### 전체 구조

```
클라이언트 (Blazor Component)
    ↓ SignalR JavaScript Client
SignalR Hub (ChatHub 확장)
    ↓
서비스 레이어 (UserService)
    ↓
Redis 데이터베이스 (RedisManager)
```

### 기술 스택

- **프론트엔드**: Blazor Server 컴포넌트 (.razor), SignalR JavaScript Client
- **백엔드**: ASP.NET Core 9.0, SignalR Hub
- **데이터베이스**: Redis (StackExchange.Redis)
- **캐싱**: FusionCache with Redis Backplane
- **로깅**: Serilog

## 컴포넌트 및 인터페이스

### 1. 프론트엔드 컴포넌트

#### 1.1 Login.razor
로그인 페이지 Blazor 컴포넌트

**위치**: `Rps/Pages/Login.razor`

**주요 기능**:
- 닉네임 입력 폼
- 입력 검증 (2-20자, 공백 불가)
- SignalR 연결 상태 표시
- 로그인 버튼 및 재시도 기능

**상태 관리**:
```csharp
private string nickname = "";
private string errorMessage = "";
private string statusMessage = "연결 대기 중";
private bool isConnecting = false;
private bool isConnected = false;
```

**SignalR 통신**:
- JavaScript Interop을 통해 SignalR 클라이언트 제어
- Hub 메서드 호출: `LoginUser(nickname)`
- Hub 이벤트 수신: `OnLoginSuccess`, `OnLoginFailed`

#### 1.2 Lobby.razor
로비 페이지 Blazor 컴포넌트

**위치**: `Rps/Pages/Lobby.razor`

**주요 기능**:
- 사용자 정보 표시 (닉네임, ID)
- 게임 통계 표시 (승/패/무, 가위/바위/보 횟수)
- 손모양 스킨 선택 UI (2가지 옵션)
- 게임 시작 버튼

**상태 관리**:
```csharp
private long userId;
private string nickname = "";
private UserStatistics? statistics;
private int selectedSkin = 0;
private bool isSkinSelected = false;
```

#### 1.3 JavaScript 파일

**위치**: `Rps/wwwroot/js/login.js`

**주요 기능**:
- SignalR 연결 관리
- Hub 메서드 호출 래퍼
- Blazor와의 상호작용을 위한 콜백 함수

```javascript
var loginConnection = null;

function initializeSignalR() {
    loginConnection = new signalR.HubConnectionBuilder()
        .withUrl("/chathub")
        .build();
    
    loginConnection.on("OnLoginSuccess", (userId, nickname, stats) => {
        DotNet.invokeMethodAsync('Rps', 'HandleLoginSuccess', userId, nickname, stats);
    });
    
    loginConnection.on("OnLoginFailed", (errorMessage) => {
        DotNet.invokeMethodAsync('Rps', 'HandleLoginFailed', errorMessage);
    });
}

async function connectToHub() {
    await loginConnection.start();
}

async function loginUser(nickname) {
    await loginConnection.invoke("LoginUser", nickname);
}
```

### 2. 백엔드 컴포넌트

#### 2.1 ChatHub 확장

**위치**: `Rps/Hubs/ChatHub.cs`

**추가 메서드**:

```csharp
public async Task LoginUser(string nickname)
{
    try
    {
        var userService = _provider.GetRequiredService<IUserService>();
        var userProfile = await userService.LoginOrCreateUserAsync(nickname, Context.ConnectionId);
        
        await Clients.Caller.SendAsync("OnLoginSuccess", 
            userProfile.UserId, 
            userProfile.Nickname, 
            userProfile.Statistics);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Login failed for nickname: {Nickname}", nickname);
        await Clients.Caller.SendAsync("OnLoginFailed", ex.Message);
    }
}

public async Task SelectSkin(long userId, int skinId)
{
    try
    {
        var userService = _provider.GetRequiredService<IUserService>();
        await userService.UpdateUserSkinAsync(userId, skinId);
        await Clients.Caller.SendAsync("OnSkinSelected", skinId);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Skin selection failed for user: {UserId}", userId);
        await Clients.Caller.SendAsync("OnSkinSelectionFailed", ex.Message);
    }
}
```

#### 2.2 UserService

**위치**: `Rps/Services/UserService.cs`

**인터페이스**:
```csharp
public interface IUserService
{
    Task<UserProfile> LoginOrCreateUserAsync(string nickname, string connectionId);
    Task<UserProfile?> GetUserByNicknameAsync(string nickname);
    Task<UserProfile?> GetUserByIdAsync(long userId);
    Task UpdateUserSkinAsync(long userId, int skinId);
    Task UpdateUserStatisticsAsync(long userId, GameResult result, HandShape choice);
}
```

**구현**:
```csharp
public class UserService : IUserService
{
    private readonly RedisManager _redisManager;
    private readonly ILogger<UserService> _logger;
    
    public async Task<UserProfile> LoginOrCreateUserAsync(string nickname, string connectionId)
    {
        var db = _redisManager.GetDatabase();
        
        // 닉네임으로 사용자 ID 조회
        var userIdStr = await db.StringGetAsync($"user:nickname:{nickname}");
        
        if (userIdStr.IsNullOrEmpty)
        {
            // 새 사용자 생성
            var userId = await db.StringIncrementAsync("user:id:counter");
            
            var userProfile = new UserProfile
            {
                UserId = userId,
                Nickname = nickname,
                ConnectionId = connectionId,
                Statistics = new UserStatistics()
            };
            
            // Redis에 저장
            await SaveUserProfileAsync(db, userProfile);
            await db.StringSetAsync($"user:nickname:{nickname}", userId);
            
            return userProfile;
        }
        else
        {
            // 기존 사용자 로드
            var userId = (long)userIdStr;
            var userProfile = await GetUserByIdAsync(userId);
            
            if (userProfile != null)
            {
                // ConnectionId 업데이트
                userProfile.ConnectionId = connectionId;
                await db.HashSetAsync($"user:{userId}", "ConnectionId", connectionId);
            }
            
            return userProfile ?? throw new Exception("사용자 프로필을 찾을 수 없습니다");
        }
    }
    
    private async Task SaveUserProfileAsync(IDatabase db, UserProfile profile)
    {
        var hashEntries = new HashEntry[]
        {
            new HashEntry("UserId", profile.UserId),
            new HashEntry("Nickname", profile.Nickname),
            new HashEntry("ConnectionId", profile.ConnectionId),
            new HashEntry("SelectedSkin", profile.SelectedSkin),
            new HashEntry("Wins", profile.Statistics.Wins),
            new HashEntry("Losses", profile.Statistics.Losses),
            new HashEntry("Draws", profile.Statistics.Draws),
            new HashEntry("RockCount", profile.Statistics.RockCount),
            new HashEntry("PaperCount", profile.Statistics.PaperCount),
            new HashEntry("ScissorsCount", profile.Statistics.ScissorsCount)
        };
        
        await db.HashSetAsync($"user:{profile.UserId}", hashEntries);
    }
}
```

## 데이터 모델

### 1. UserProfile

```csharp
public class UserProfile
{
    public long UserId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public int SelectedSkin { get; set; } = 0;
    public UserStatistics Statistics { get; set; } = new();
}
```

### 2. UserStatistics

```csharp
public class UserStatistics
{
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public int RockCount { get; set; }
    public int PaperCount { get; set; }
    public int ScissorsCount { get; set; }
    
    public int TotalGames => Wins + Losses + Draws;
    public double WinRate => TotalGames > 0 ? (double)Wins / TotalGames * 100 : 0;
}
```

### 3. Enums

```csharp
public enum GameResult
{
    Win,
    Loss,
    Draw
}

public enum HandShape
{
    Rock,
    Paper,
    Scissors
}
```

## Redis 데이터 구조

### 1. 사용자 ID 카운터
```
Key: user:id:counter
Type: String (Integer)
Purpose: 고유한 사용자 ID 생성
```

### 2. 닉네임 → 사용자 ID 매핑
```
Key: user:nickname:{nickname}
Type: String
Value: {userId}
Purpose: 닉네임으로 사용자 ID 조회
```

### 3. 사용자 프로필
```
Key: user:{userId}
Type: Hash
Fields:
  - UserId: {userId}
  - Nickname: {nickname}
  - ConnectionId: {connectionId}
  - SelectedSkin: {skinId}
  - Wins: {wins}
  - Losses: {losses}
  - Draws: {draws}
  - RockCount: {rockCount}
  - PaperCount: {paperCount}
  - ScissorsCount: {scissorsCount}
```

### 4. 활성 연결
```
Key: Users (기존 ChatHub에서 사용 중)
Type: Set
Members: {connectionId}
Purpose: 현재 연결된 사용자 추적
```

## 에러 처리

### 1. 클라이언트 측 에러

**검증 에러**:
- 닉네임 길이 부족 (< 2자): "닉네임은 최소 2자 이상이어야 합니다"
- 닉네임 길이 초과 (> 20자): "닉네임은 최대 20자까지 가능합니다"
- 공백만 포함: "닉네임에 공백만 사용할 수 없습니다"

**연결 에러**:
- SignalR 연결 실패: "서버에 연결할 수 없습니다. 다시 시도해주세요"
- 타임아웃: "연결 시간이 초과되었습니다"

### 2. 서버 측 에러

**Redis 에러**:
- 연결 실패: Redis 연결 상태 확인 및 재시도
- 데이터 저장 실패: 로그 기록 및 클라이언트에 에러 메시지 전송

**Hub 에러**:
- 모든 예외를 catch하여 로그 기록
- 클라이언트에 사용자 친화적인 에러 메시지 전송
- 연결 상태 복구 메커니즘

## 테스트 전략

### 1. 단위 테스트

**UserService 테스트**:
- 새 사용자 생성 테스트
- 기존 사용자 로그인 테스트
- 통계 업데이트 테스트
- 스킨 선택 테스트

**검증 로직 테스트**:
- 닉네임 길이 검증
- 공백 검증
- 특수문자 처리

### 2. 통합 테스트

**SignalR Hub 테스트**:
- LoginUser 메서드 호출 테스트
- SelectSkin 메서드 호출 테스트
- 이벤트 전송 테스트

**Redis 통합 테스트**:
- 사용자 프로필 저장/조회 테스트
- ID 생성 동시성 테스트
- 닉네임 중복 처리 테스트

### 3. E2E 테스트

**사용자 플로우 테스트**:
- 로그인 → 로비 → 스킨 선택 전체 플로우
- 재연결 시나리오
- 다중 사용자 동시 접속

## 보안 고려사항

### 1. 입력 검증
- 닉네임 길이 제한 (2-20자)
- XSS 방지를 위한 HTML 인코딩
- SQL Injection 방지 (Redis는 NoSQL이지만 입력 검증 필수)

### 2. 연결 관리
- ConnectionId 기반 사용자 식별
- 중복 로그인 처리
- 세션 타임아웃 관리

### 3. 데이터 보호
- Redis 키에 환경별 prefix 적용 (기존 구현 활용)
- 민감 정보 로깅 제외
- 연결 문자열 환경 변수 관리

## 성능 최적화

### 1. Redis 최적화
- HashSet을 사용한 효율적인 데이터 구조
- Pipeline 사용으로 다중 명령 최적화
- 적절한 TTL 설정 (필요시)

### 2. SignalR 최적화
- Redis Backplane을 통한 스케일아웃 지원 (기존 구성 활용)
- 연결 재시도 로직 구현
- 메시지 크기 최소화

### 3. Blazor 최적화
- 필요한 경우에만 StateHasChanged 호출
- JavaScript Interop 호출 최소화
- 컴포넌트 렌더링 최적화

## 배포 고려사항

### 1. 환경 설정
- appsettings.{Environment}.json을 통한 환경별 설정
- Redis 연결 문자열 환경 변수화
- 로깅 레벨 환경별 조정

### 2. 모니터링
- Serilog를 통한 구조화된 로깅
- Redis 연결 상태 모니터링
- SignalR 연결 수 추적

### 3. 스케일링
- Redis Backplane을 통한 다중 서버 지원
- 상태 비저장 설계 (Redis에 모든 상태 저장)
- 로드 밸런서 호환성
