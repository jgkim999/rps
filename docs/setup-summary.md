# 프로젝트 설정 완료 요약

## 수정 내역

### 1. Login 페이지 404 오류 해결

**문제**: `/login` 접속 시 404 오류 발생

**해결**:
- Blazor 인프라 파일 생성 (App.razor, Routes.razor, _Imports.razor)
- Login.razor 및 Lobby.razor를 `Components/Pages/`로 이동
- `@rendermode InteractiveServer` 디렉티브 추가
- Program.cs에 Blazor Components 등록 추가

**결과**: `/login` 페이지 정상 작동

---

### 2. Anti-forgery 미들웨어 추가

**문제**:
```
InvalidOperationException: Endpoint /login contains anti-forgery metadata,
but a middleware was not found that supports anti-forgery.
```

**해결**:
```csharp
app.UseRouting();
app.UseAuthorization();
app.UseAntiforgery();  // 추가
```

**결과**: CSRF 공격 방지 기능 활성화

---

### 3. 외부 접속 허용 설정

**문제**: localhost에서만 접속 가능

**해결**:
```csharp
builder.WebHost.UseUrls("http://0.0.0.0:5184");
```

**결과**:
- 모든 네트워크 인터페이스에서 접속 가능
- 같은 네트워크의 다른 디바이스에서 `http://[서버IP]:5184/login` 접속 가능

---

## 현재 프로젝트 구조

```
Rps/
├── Components/
│   ├── App.razor                 # Blazor 앱 루트
│   ├── Routes.razor              # 라우터 설정
│   ├── _Imports.razor            # 공통 using
│   └── Pages/
│       ├── Login.razor           # 로그인 페이지 ✨
│       └── Lobby.razor           # 로비 페이지 ✨
├── Pages/
│   ├── Index.cshtml              # Razor Pages
│   ├── Privacy.cshtml
│   └── Error.cshtml
├── Hubs/
│   └── GameHub.cs                # SignalR Hub
├── Services/
│   ├── IUserService.cs
│   └── UserService.cs
├── Models/
│   ├── UserProfile.cs
│   ├── UserStatistics.cs
│   ├── GameResult.cs
│   └── HandShape.cs
├── Configs/
│   └── RedisConfig.cs
├── RedisManager.cs
├── Program.cs                    # 앱 설정 ✨
└── appsettings.json
```

---

## 접속 방법

### 로컬 접속
```
http://localhost:5184/login
```

### 네트워크 접속

1. **서버 IP 확인**:
   ```bash
   # macOS/Linux
   ifconfig | grep "inet "

   # Windows
   ipconfig
   ```

2. **다른 디바이스에서 접속**:
   ```
   http://192.168.0.10:5184/login  # 예시
   ```

3. **방화벽 설정** (필요시):
   - macOS: System Settings > Network > Firewall
   - Linux: `sudo ufw allow 5184/tcp`
   - Windows: Windows Defender 방화벽 > 포트 5184 허용

---

## Program.cs 최종 구조

```csharp
var builder = WebApplication.CreateBuilder(args);

// 외부 접속 허용
builder.WebHost.UseUrls("http://0.0.0.0:5184");

// 환경 설정
var environment = builder.Environment.EnvironmentName;

// Redis 설정
var redisConfig = builder.Configuration.GetSection("Redis").Get<RedisConfig>();
ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(options);
builder.Services.AddSingleton(new RedisManager(redis, environment));

// FusionCache 설정
builder.Services.AddFusionCache()
    .WithSerializer(new FusionCacheNewtonsoftJsonSerializer())
    .WithDistributedCache(new RedisCache(...))
    .WithBackplane(new RedisBackplane(...));

// 서비스 등록
builder.Services.AddRazorPages();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<IUserService, UserService>();

// SignalR 설정
builder.Services.AddSignalR()
    .AddStackExchangeRedis(redisConfig.SignalRBackplane, ...);

// CORS 설정
builder.Services.AddCors(options => {
    options.AddPolicy("AllowAll", ...);
});

var app = builder.Build();

// 미들웨어 파이프라인 (순서 중요!)
app.UseCors("AllowAll");
app.UseRouting();
app.UseAuthorization();
app.UseAntiforgery();              // ← CSRF 방지

// 엔드포인트 매핑
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapRazorComponents<Rps.Components.App>()
    .AddInteractiveServerRenderMode();
app.MapHub<GameHub>("/gamehub");

app.Run();
```

---

## 주요 기술 스택

### 백엔드
- **ASP.NET Core 9.0**: 웹 프레임워크
- **SignalR**: 실시간 양방향 통신
- **Redis**:
  - SignalR 백플레인 (다중 서버 지원)
  - FusionCache 분산 캐시
  - 사용자 데이터 영구 저장
- **Serilog**: 구조화된 로깅
- **OpenTelemetry**: 분산 추적

### 프론트엔드
- **Blazor Interactive Server**: SPA 스타일 UI
- **Razor Pages**: 기존 페이지용

### 인프라
- **FusionCache**: 2단계 캐싱 (메모리 + Redis)
- **RedisManager**: 환경별 키 네임스페이스 자동 관리

---

## 빌드 및 실행

```bash
# 빌드
dotnet build Rps.sln

# 실행
dotnet run --project Rps/Rps.csproj

# 로그 확인
tail -f Rps/logs/rps-*.log
```

**빌드 결과**:
```
빌드했습니다.
    경고 5개
    오류 0개
```

---

## 보안 고려사항

### 개발 환경 (현재)
- HTTP만 사용
- CORS AllowAll 정책
- 모든 IP에서 접속 허용

### 프로덕션 배포 시 필수 변경사항

1. **HTTPS 사용**:
   ```csharp
   builder.WebHost.UseUrls("https://0.0.0.0:5184");
   // SSL 인증서 설정 추가
   ```

2. **CORS 정책 강화**:
   ```csharp
   options.AddPolicy("Production",
       policy => policy.WithOrigins("https://yourdomain.com")
           .AllowAnyMethod()
           .AllowAnyHeader());
   ```

3. **방화벽 규칙**:
   - 신뢰할 수 있는 IP만 허용
   - DDoS 방어 설정
   - Rate Limiting 추가

4. **환경 변수로 설정 관리**:
   - Redis 연결 문자열
   - 민감한 설정값

---

## 문서

- **CLAUDE.md**: 프로젝트 아키텍처 및 개발 가이드
- **docs/login-404-fix.md**: Login 페이지 404 오류 해결 상세 가이드
- **docs/external-access-and-antiforgery-fix.md**: 외부 접속 및 Anti-forgery 설정 가이드
- **docs/setup-summary.md**: 이 문서

---

## 다음 단계

1. ✅ Blazor 인프라 설정 완료
2. ✅ Anti-forgery 미들웨어 추가 완료
3. ✅ 외부 접속 허용 완료
4. ⏳ Login 페이지 JavaScript 연동 (login.js)
5. ⏳ Lobby 페이지 기능 구현
6. ⏳ 게임 로직 구현

---

## 알려진 경고

현재 5개의 경고가 있지만 기능에 영향 없음:

1. `CS1998`: async 메서드에 await 없음 (향후 확장 대비)
2. `CS0168`: 예외 변수 미사용 (로깅 추가 권장)
3. `CS0618`: Redis ChannelPrefix 사용 방식 (RedisChannel.Literal() 권장)

이러한 경고들은 향후 코드 품질 개선 시 수정 예정입니다.

---

## 테스트 체크리스트

- [x] 빌드 성공
- [x] `/login` 페이지 접속 가능
- [x] localhost에서 접속 가능
- [x] 네트워크 IP로 접속 가능
- [ ] SignalR 연결 테스트
- [ ] Redis 연결 테스트
- [ ] 로그인 기능 테스트
- [ ] 로비 기능 테스트

---

## 지원 및 문의

- **문서**: `CLAUDE.md`, `docs/` 디렉터리
- **로그**: `Rps/logs/rps-*.log`
- **설정**: `Rps/appsettings.json`