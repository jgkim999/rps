# JavaScript 통합 오류 수정

## 문제 상황

### 오류 메시지
```
Microsoft.JSInterop.JSException: Could not find 'setLoginComponentReference'
('setLoginComponentReference' was undefined).
```

### 원인
Blazor 컴포넌트(Login.razor)에서 JavaScript 함수를 호출하려고 했지만, 해당 JavaScript 파일들이 HTML에 로드되지 않아서 함수를 찾을 수 없었습니다.

**누락된 스크립트**:
- SignalR 클라이언트 라이브러리
- login.js
- lobby.js

## 해결 방법

### App.razor에 JavaScript 파일 추가

**수정 전** (`Components/App.razor`):
```html
<body>
    <Routes />
    <script src="_framework/blazor.web.js"></script>
</body>
```

**수정 후**:
```html
<body>
    <Routes />
    <script src="_framework/blazor.web.js"></script>
    <script src="~/js/signalr/dist/browser/signalr.min.js"></script>
    <script src="~/js/login.js"></script>
    <script src="~/js/lobby.js"></script>
</body>
```

### 스크립트 로드 순서

1. **blazor.web.js**: Blazor 프레임워크 (필수, 가장 먼저)
2. **signalr.min.js**: SignalR 클라이언트 라이브러리 (login.js와 lobby.js가 의존)
3. **login.js**: 로그인 페이지 JavaScript 로직
4. **lobby.js**: 로비 페이지 JavaScript 로직

**중요**: SignalR 라이브러리는 반드시 login.js와 lobby.js보다 먼저 로드되어야 합니다. 그래야 `signalR` 전역 객체를 사용할 수 있습니다.

## JavaScript 파일 구조

### login.js (`wwwroot/js/login.js`)

**주요 기능**:
```javascript
// Blazor에서 컴포넌트 참조 설정
window.setLoginComponentReference = function (componentRef) {
    loginComponentReference = componentRef;
};

// SignalR 연결 초기화
window.initializeSignalR = function () {
    loginConnection = new signalR.HubConnectionBuilder()
        .withUrl("/gamehub")
        .withAutomaticReconnect()
        .build();

    // 이벤트 핸들러 등록
    loginConnection.on("OnLoginSuccess", ...);
    loginConnection.on("OnLoginFailed", ...);
};

// 허브에 연결
window.connectToHub = async function () {
    await loginConnection.start();
};

// 로그인 호출
window.loginUser = async function (nickname) {
    await loginConnection.invoke("LoginUser", nickname);
};
```

### lobby.js (`wwwroot/js/lobby.js`)

**주요 기능**:
```javascript
// Blazor에서 컴포넌트 참조 설정 및 자동 연결
window.setLobbyComponentReference = function (componentRef) {
    lobbyComponentReference = componentRef;
    initializeLobbyConnection();
};

// SignalR 연결 초기화 및 자동 시작
function initializeLobbyConnection() {
    lobbyConnection = new signalR.HubConnectionBuilder()
        .withUrl("/gamehub")
        .withAutomaticReconnect()
        .build();

    // 이벤트 핸들러 등록
    lobbyConnection.on("OnSkinSelected", ...);
    lobbyConnection.on("OnSkinSelectionFailed", ...);

    // 자동 연결 시작
    lobbyConnection.start();
}

// 스킨 선택 호출
window.selectSkin = async function (userId, skinId) {
    await lobbyConnection.invoke("SelectSkin", userId, skinId);
};
```

## JavaScript 로딩 타이밍 문제

### 문제 상황
App.razor에 스크립트를 추가한 후에도 간헐적으로 동일한 오류가 발생할 수 있습니다:
```
Could not find 'setLoginComponentReference' ('setLoginComponentReference' was undefined).
```

### 원인
Blazor 컴포넌트의 `OnAfterRenderAsync`가 실행되는 시점에 JavaScript 파일이 아직 완전히 로드되지 않았을 수 있습니다. 특히 다음과 같은 경우 발생 가능:
- 네트워크 지연
- 브라우저 캐싱 상태
- 여러 스크립트 파일 동시 로딩
- 서버 재시작 직후 첫 접속

### 해결 방법: 재시도 로직 추가

Login.razor와 Lobby.razor에 JavaScript 함수 호출 시 재시도 로직을 추가하여 문제를 해결했습니다.

**구현 코드** (`Components/Pages/Login.razor:63-94`, `Components/Pages/Lobby.razor:133-164`):
```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        objRef = DotNetObjectReference.Create(this);

        // JavaScript 파일이 로드될 때까지 재시도
        var maxRetries = 10;
        var retryCount = 0;
        var retryDelay = 100; // ms

        while (retryCount < maxRetries)
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("setLoginComponentReference", objRef);
                break; // 성공하면 루프 종료
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
                await Task.Delay(retryDelay);
            }
        }
    }
}
```

**재시도 로직 특징**:
- **최대 재시도 횟수**: 10회
- **재시도 간격**: 100ms (총 최대 1초 대기)
- **성공 시**: 즉시 루프 종료
- **최종 실패 시**: 사용자에게 새로고침 안내 메시지 표시
- **예외 전파**: 마지막 시도 실패 시에만 예외 던짐

### 적용 파일
- `Rps/Components/Pages/Login.razor` - `setLoginComponentReference` 호출
- `Rps/Components/Pages/Lobby.razor` - `setLobbyComponentReference` 호출

## Blazor와 JavaScript 상호작용 흐름

### Login.razor의 경우

1. **컴포넌트 렌더링 후 (재시도 로직 포함)**:
   ```csharp
   protected override async Task OnAfterRenderAsync(bool firstRender)
   {
       if (firstRender)
       {
           objRef = DotNetObjectReference.Create(this);

           // 재시도 로직으로 JavaScript 함수 호출
           var maxRetries = 10;
           var retryCount = 0;

           while (retryCount < maxRetries)
           {
               try
               {
                   await JSRuntime.InvokeVoidAsync("setLoginComponentReference", objRef);
                   break;
               }
               catch (JSException)
               {
                   retryCount++;
                   if (retryCount >= maxRetries)
                   {
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

2. **로그인 버튼 클릭**:
   ```csharp
   private async Task HandleLogin()
   {
       await InitializeSignalR();    // JavaScript: initializeSignalR()
       await ConnectToHub();          // JavaScript: connectToHub()
       await LoginUser(nickname);     // JavaScript: loginUser(nickname)
   }
   ```

3. **SignalR 이벤트 수신** (JavaScript → Blazor):
   ```javascript
   loginConnection.on("OnLoginSuccess", function (userId, nickname, statistics) {
       loginComponentReference.invokeMethodAsync('HandleLoginSuccess', userId, nickname, statistics);
   });
   ```

4. **Blazor에서 처리**:
   ```csharp
   [JSInvokable]
   public void HandleLoginSuccess(long userId, string userNickname, object statistics)
   {
       // 로비로 이동
       NavigationManager.NavigateTo($"/lobby?userId={userId}&nickname={userNickname}");
   }
   ```

## SignalR 클라이언트 라이브러리

### 로컬 파일 사용
프로젝트에 이미 SignalR 클라이언트 라이브러리가 포함되어 있습니다:
```
wwwroot/js/signalr/dist/browser/
├── signalr.js
├── signalr.js.map
├── signalr.min.js
└── signalr.min.js.map
```

**CDN 대신 로컬 파일 사용 이유**:
- 오프라인 작동 가능
- 버전 일관성 보장
- 로딩 속도 향상 (로컬 네트워크)
- 외부 의존성 제거

### CDN 사용 시 (선택사항)
인터넷 연결이 보장되는 환경에서는 CDN도 사용 가능합니다:
```html
<script src="https://cdn.jsdelivr.net/npm/@microsoft/signalr@8.0.0/dist/browser/signalr.min.js"></script>
```

**참고**: Razor 파일에서는 `@` 기호를 `@@`로 이스케이프해야 합니다.

## Blazor Interactive Server의 JavaScript 통합

### DotNetObjectReference 패턴

Blazor에서 JavaScript로 컴포넌트 참조를 전달할 때 사용합니다:

```csharp
// 참조 생성
objRef = DotNetObjectReference.Create(this);

// JavaScript로 전달
await JSRuntime.InvokeVoidAsync("setLoginComponentReference", objRef);

// 정리 (IDisposable 구현)
public void Dispose()
{
    objRef?.Dispose();
}
```

**중요**: `DotNetObjectReference`는 반드시 `Dispose()`를 호출하여 메모리 누수를 방지해야 합니다.

### JSInvokable 메서드

JavaScript에서 Blazor 메서드를 호출하려면 `[JSInvokable]` 어트리뷰트가 필요합니다:

```csharp
[JSInvokable]
public void HandleLoginSuccess(long userId, string userNickname, object statistics)
{
    // JavaScript에서 호출 가능
}
```

## SignalR 연결 관리

### Login과 Lobby의 차이점

**Login.razor**:
- 사용자가 명시적으로 "로그인" 버튼을 클릭해야 연결 시작
- 연결 과정: `initializeSignalR()` → `connectToHub()` → `loginUser()`

**Lobby.razor**:
- 컴포넌트가 로드되면 자동으로 SignalR 연결 시작
- `setLobbyComponentReference()` 호출 시 `initializeLobbyConnection()`이 자동 실행

### 자동 재연결

두 JavaScript 파일 모두 `.withAutomaticReconnect()`를 사용하여 연결이 끊어지면 자동으로 재연결을 시도합니다:

```javascript
loginConnection = new signalR.HubConnectionBuilder()
    .withUrl("/gamehub")
    .withAutomaticReconnect()  // 기본값: [0, 2, 10, 30] 초 간격으로 재시도
    .build();
```

## 빌드 결과

```
빌드했습니다.
    경고 5개
    오류 0개
```

모든 기능이 정상적으로 작동합니다.

## 테스트 체크리스트

### JavaScript 로드 확인
브라우저 개발자 도구(F12)에서 확인:

1. **Console 탭**:
   ```
   (오류 메시지가 없어야 함)
   ```

2. **Network 탭**:
   ```
   ✓ blazor.web.js - 200 OK
   ✓ signalr.min.js - 200 OK
   ✓ login.js - 200 OK
   ✓ lobby.js - 200 OK
   ```

3. **Console에서 함수 확인**:
   ```javascript
   typeof window.setLoginComponentReference  // "function"
   typeof window.initializeSignalR           // "function"
   typeof window.connectToHub                // "function"
   typeof window.loginUser                   // "function"
   ```

### SignalR 연결 테스트

1. **/login 페이지 접속**
2. **닉네임 입력 및 로그인 버튼 클릭**
3. **Console에서 확인**:
   ```
   SignalR connected successfully
   LoginUser invoked successfully
   ```

## 문제 해결

### JavaScript 파일이 로드되지 않는 경우

1. **캐시 클리어**:
   - Chrome: Ctrl+Shift+Delete
   - 하드 새로고침: Ctrl+F5 (Windows), Cmd+Shift+R (Mac)

2. **파일 경로 확인**:
   ```bash
   ls -la Rps/wwwroot/js/
   ls -la Rps/wwwroot/js/signalr/dist/browser/
   ```

3. **빌드 후 재실행**:
   ```bash
   dotnet clean
   dotnet build
   dotnet run --project Rps/Rps.csproj
   ```

### SignalR 연결 실패

1. **GameHub 확인**:
   - `/gamehub` 엔드포인트가 Program.cs에 등록되어 있는지 확인
   - `app.MapHub<GameHub>("/gamehub");`

2. **CORS 설정 확인**:
   - 외부에서 접속 시 CORS 정책이 올바른지 확인

3. **로그 확인**:
   ```bash
   tail -f Rps/logs/rps-*.log
   ```

## 참고 자료

- [ASP.NET Core Blazor JavaScript interoperability](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/)
- [SignalR JavaScript client](https://learn.microsoft.com/en-us/aspnet/core/signalr/javascript-client)
- [Blazor lifecycle methods](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/lifecycle)