# 외부 접속 허용 및 Anti-forgery 오류 수정

## 문제 상황

### 오류 메시지
```
InvalidOperationException: Endpoint /login (/login) contains anti-forgery metadata,
but a middleware was not found that supports anti-forgery.
Configure your application startup by adding app.UseAntiforgery() in the application startup code.
```

### 요구사항
- Anti-forgery 미들웨어 누락으로 인한 오류 해결
- 로컬 네트워크 내 모든 디바이스에서 접속 가능하도록 설정

## 원인 분석

### 1. Anti-forgery 미들웨어 누락
Blazor Interactive Server 모드를 사용할 때는 CSRF(Cross-Site Request Forgery) 공격을 방지하기 위한 anti-forgery 토큰이 자동으로 활성화됩니다. 하지만 이를 처리할 미들웨어(`app.UseAntiforgery()`)가 설정되지 않아 오류가 발생했습니다.

**미들웨어 추가 위치 규칙**:
- `app.UseRouting()` 이후
- `app.UseAuthorization()` 이후
- `app.MapRazorComponents()` 이전

### 2. 로컬 네트워크 접속 제한
기본적으로 ASP.NET Core는 `localhost`(127.0.0.1)에서만 리스닝하도록 설정되어 있어, 같은 네트워크의 다른 디바이스에서 접속할 수 없습니다.

## 해결 방법

### 1. Anti-forgery 미들웨어 추가

**Program.cs 수정**:
```csharp
app.UseRouting();

app.UseAuthorization();

app.UseAntiforgery();  // 추가

app.MapStaticAssets();
```

**위치가 중요한 이유**:
- `UseRouting()` 이후: 라우팅 정보가 있어야 엔드포인트별 anti-forgery 설정 확인 가능
- `UseAuthorization()` 이후: 인증/인가 후 토큰 검증이 이루어져야 함
- 매핑 메서드 이전: 엔드포인트 매핑 전에 미들웨어가 등록되어야 함

### 2. 외부 접속 허용 설정

**Program.cs에 Kestrel URL 바인딩 추가**:
```csharp
var builder = WebApplication.CreateBuilder(args);

// 모든 네트워크 인터페이스에서 접속 허용
builder.WebHost.UseUrls("http://0.0.0.0:5184");
```

**설정 설명**:
- `0.0.0.0`: 모든 네트워크 인터페이스에서 리스닝
- `:5184`: 포트 번호 (기존과 동일)
- `http://`: HTTPS 없이 HTTP만 사용 (개발 환경용)

## 수정 후 코드

### Program.cs 전체 구조
```csharp
var builder = WebApplication.CreateBuilder(args);

// 외부 접속 허용
builder.WebHost.UseUrls("http://0.0.0.0:5184");

// ... 환경 설정, 서비스 등록 ...

var app = builder.Build();

// 미들웨어 파이프라인 (순서 중요!)
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseCors("AllowAll");
app.UseRouting();
app.UseAuthorization();
app.UseAntiforgery();           // ← 추가됨

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapRazorComponents<Rps.Components.App>()
    .AddInteractiveServerRenderMode();
app.MapHub<GameHub>("/gamehub");

app.Run();
```

## 접속 방법

### 1. 로컬 접속
```
http://localhost:5184/login
```

### 2. 같은 네트워크의 다른 디바이스에서 접속

**서버 IP 확인** (macOS/Linux):
```bash
ifconfig | grep "inet "
```

**서버 IP 확인** (Windows):
```cmd
ipconfig
```

**접속 URL**:
```
http://[서버IP]:5184/login

예시:
http://192.168.0.10:5184/login
http://10.0.0.5:5184/login
```

### 3. 방화벽 설정

외부 접속이 안 될 경우, 방화벽에서 포트 5184를 허용해야 합니다.

**macOS**:
```bash
# 방화벽 상태 확인
sudo /usr/libexec/ApplicationFirewall/socketfilterfw --getglobalstate

# 특정 앱 허용 (필요시)
sudo /usr/libexec/ApplicationFirewall/socketfilterfw --add /usr/local/share/dotnet/dotnet
```

**Linux (ufw)**:
```bash
sudo ufw allow 5184/tcp
```

**Windows**:
1. Windows Defender 방화벽 > 고급 설정
2. 인바운드 규칙 > 새 규칙
3. 포트 > TCP > 5184
4. 연결 허용 > 규칙 추가

## 보안 고려사항

### 개발 환경 전용 설정
현재 설정은 **개발 환경 전용**입니다. 프로덕션 배포 시 다음 사항을 반드시 검토하세요:

1. **HTTPS 사용**:
   ```csharp
   builder.WebHost.UseUrls("https://0.0.0.0:5184");
   // SSL 인증서 설정 필요
   ```

2. **CORS 정책 강화**:
   현재 `AllowAll` 정책은 모든 출처를 허용합니다. 프로덕션에서는 특정 도메인만 허용하도록 수정:
   ```csharp
   builder.Services.AddCors(options =>
   {
       options.AddPolicy("Production",
           policy => policy.WithOrigins("https://yourdomain.com")
               .AllowAnyMethod()
               .AllowAnyHeader());
   });
   ```

3. **방화벽 설정**:
   - 신뢰할 수 있는 네트워크만 접근 허용
   - 불필요한 포트는 닫기
   - DDoS 방어 설정

4. **Rate Limiting**:
   ASP.NET Core의 Rate Limiting 미들웨어 사용 권장

## 테스트

### 1. 로컬 테스트
```bash
# 애플리케이션 실행
dotnet run --project Rps/Rps.csproj

# 브라우저에서 확인
http://localhost:5184/login
```

### 2. 네트워크 테스트
```bash
# 다른 디바이스에서 서버 IP로 접속
http://[서버IP]:5184/login

# curl로 테스트
curl http://[서버IP]:5184/login
```

### 3. 로그 확인
```bash
# 로그 파일 확인
tail -f Rps/logs/rps-*.log

# 콘솔에서 다음 로그가 보여야 함:
# "Now listening on: http://0.0.0.0:5184"
```

## 빌드 결과

```
빌드했습니다.
    경고 5개
    오류 0개
```

모든 기능이 정상적으로 작동합니다.

## Anti-forgery 토큰 작동 방식

### 1. 토큰 생성
Blazor Interactive Server 모드에서 페이지가 렌더링될 때, 서버는 자동으로 anti-forgery 토큰을 생성하여 HTML에 포함합니다.

```html
<input name="__RequestVerificationToken" type="hidden" value="CfDJ8..." />
```

### 2. 토큰 검증
POST 요청이 들어오면, `UseAntiforgery()` 미들웨어가 토큰을 검증합니다:
- 쿠키의 토큰과 요청의 토큰이 일치하는지 확인
- 토큰이 유효한 시간 내에 생성되었는지 확인
- 토큰이 현재 사용자와 연결되어 있는지 확인

### 3. Blazor의 자동 처리
Blazor Interactive Server는 SignalR을 통해 통신하므로, anti-forgery 토큰이 자동으로 WebSocket 연결에 포함됩니다. 개발자가 직접 토큰을 관리할 필요가 없습니다.

## 참고 자료

- [ASP.NET Core Anti-forgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery)
- [Blazor Security](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/)
- [Kestrel Web Server Configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints)