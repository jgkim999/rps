# Login 페이지 404 오류 해결

## 문제 상황
- URL: `http://localhost:5184/login`
- 오류: `404 Not Found`
- 로그: `[2025-11-08 08:06:55.821 +09:00 INF] Request finished HTTP/1.1 GET http://localhost:5184/login - 404 0 null 1.8109ms`

## 원인 분석

프로젝트에 Blazor 컴포넌트(`Login.razor`, `Lobby.razor`)가 존재했지만, 이를 라우팅하고 렌더링할 수 있는 Blazor 인프라가 설정되지 않았습니다.

### 누락된 구성 요소:
1. **App.razor**: Blazor 앱의 루트 컴포넌트
2. **Routes.razor**: Blazor 라우터 설정
3. **_Imports.razor**: 공통 using 지시문
4. **Program.cs 설정**: Blazor Components 등록 및 매핑
5. **컴포넌트 위치**: Pages/ 디렉터리가 아닌 Components/Pages/ 디렉터리 필요

## 해결 방법

### 1. Blazor 인프라 파일 생성

#### `Components/App.razor` 생성
```razor
<!DOCTYPE html>
<html lang="ko">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <link rel="stylesheet" href="~/css/site.css" />
    <HeadOutlet />
</head>
<body>
    <Routes />
    <script src="_framework/blazor.web.js"></script>
</body>
</html>
```

#### `Components/Routes.razor` 생성
```razor
@using Microsoft.AspNetCore.Components.Routing

<Router AppAssembly="@typeof(Program).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="@routeData" />
    </Found>
    <NotFound>
        <PageTitle>Not found</PageTitle>
        <div class="text-center mt-5">
            <h1>404 - 페이지를 찾을 수 없습니다</h1>
            <p>요청하신 페이지가 존재하지 않습니다.</p>
            <a href="/login" class="btn btn-primary">로그인 페이지로 이동</a>
        </div>
    </NotFound>
</Router>
```

#### `Components/_Imports.razor` 생성
```razor
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Components
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using static Microsoft.AspNetCore.Components.Web.RenderMode
@using Microsoft.JSInterop
@using Rps
@using Rps.Components
@using Rps.Models
@using Rps.Services
```

**중요**: `@using static Microsoft.AspNetCore.Components.Web.RenderMode` 추가로 `InteractiveServer` 사용 가능

### 2. Blazor 컴포넌트 이동
```bash
# Pages/에서 Components/Pages/로 이동
mv Rps/Pages/Login.razor Rps/Components/Pages/Login.razor
mv Rps/Pages/Lobby.razor Rps/Components/Pages/Lobby.razor
```

### 3. 컴포넌트에 렌더 모드 추가

각 `.razor` 파일에 `@rendermode InteractiveServer` 디렉티브 추가:

**Login.razor**:
```razor
@page "/login"
@rendermode InteractiveServer
@using Microsoft.JSInterop
...
```

**Lobby.razor**:
```razor
@page "/lobby"
@rendermode InteractiveServer
@using Microsoft.JSInterop
...
```

### 4. Program.cs 수정

**변경 전**:
```csharp
// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
```

**변경 후**:
```csharp
// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
```

**매핑 변경 전**:
```csharp
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapBlazorHub();
app.MapHub<GameHub>("/gamehub");
```

**매핑 변경 후**:
```csharp
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapRazorComponents<Rps.Components.App>()
    .AddInteractiveServerRenderMode();
app.MapHub<GameHub>("/gamehub");
```

## 수정 결과

### 빌드 성공
```
빌드했습니다.
    경고 5개
    오류 0개
```

### 작동 확인
- `/login` 경로로 접속 시 Login.razor 컴포넌트가 정상적으로 렌더링됨
- Blazor Interactive Server 모드로 작동하여 사용자 입력 및 이벤트 처리 가능
- SignalR 연결을 통한 실시간 통신 가능

## 기술적 배경

### .NET 9.0 Blazor 아키텍처
.NET 9.0에서는 Blazor의 렌더링 모드가 다음과 같이 구분됩니다:

1. **Static Server Rendering (SSR)**: 서버에서 HTML 생성, 인터랙티브하지 않음
2. **Interactive Server**: SignalR을 통한 실시간 인터랙티브 모드
3. **Interactive WebAssembly**: 클라이언트 측 WebAssembly 실행
4. **Interactive Auto**: Server와 WebAssembly를 자동으로 전환

### 프로젝트 구조
```
Rps/
├── Components/
│   ├── App.razor                 # Blazor 앱 루트
│   ├── Routes.razor              # 라우터 설정
│   ├── _Imports.razor            # 공통 using
│   └── Pages/
│       ├── Login.razor           # 로그인 페이지
│       └── Lobby.razor           # 로비 페이지
├── Pages/
│   ├── Index.cshtml              # Razor Pages (기존)
│   ├── Privacy.cshtml
│   └── Error.cshtml
└── Program.cs                    # 앱 구성
```

### Razor Pages vs Blazor Components
- **Razor Pages (.cshtml)**: 전통적인 서버 사이드 페이지, 각 요청마다 새로운 페이지 로드
- **Blazor Components (.razor)**: SPA 스타일의 컴포넌트, 부분 업데이트 및 실시간 인터랙션

## 추가 개선 사항

현재 남은 경고들:
1. `CS1998`: `OnInitializedAsync`에 await가 없음 - 정상 (향후 비동기 작업 추가 시 사용)
2. `CS0168`: 예외 변수 미사용 - 로깅에 사용하도록 개선 권장
3. `CS0618`: Redis ChannelPrefix 사용 방식 - `RedisChannel.Literal()` 사용 권장

이러한 경고들은 기능에 영향을 주지 않지만, 향후 코드 품질 개선을 위해 수정할 수 있습니다.

## 참고 자료
- [ASP.NET Core Blazor render modes](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes)
- [Blazor routing and navigation](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/routing)