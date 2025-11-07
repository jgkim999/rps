# 구현 작업 목록

- [x] 1. 데이터 모델 및 서비스 인터페이스 생성
  - UserProfile, UserStatistics, GameResult, HandShape 모델 클래스 작성
  - IUserService 인터페이스 정의
  - _요구사항: 5.3, 6.2, 6.3_

- [x] 2. UserService 구현
  - [x] 2.1 LoginOrCreateUserAsync 메서드 구현
    - Redis에서 닉네임으로 사용자 조회
    - 신규 사용자인 경우 고유 ID 생성 및 프로필 생성
    - 기존 사용자인 경우 프로필 로드 및 ConnectionId 업데이트
    - _요구사항: 5.1, 5.2, 5.3, 5.4, 5.5_

  - [x] 2.2 GetUserByIdAsync 및 GetUserByNicknameAsync 메서드 구현
    - Redis HashSet에서 사용자 프로필 조회
    - UserProfile 객체로 변환
    - _요구사항: 5.5, 6.1_

  - [x] 2.3 UpdateUserSkinAsync 메서드 구현
    - Redis에 선택된 스킨 ID 저장
    - _요구사항: 8.4_

  - [x] 2.4 UpdateUserStatisticsAsync 메서드 구현
    - 게임 결과에 따라 승/패/무 통계 업데이트
    - 선택한 손모양 통계 업데이트
    - _요구사항: 6.1, 6.2, 6.3_

  - [x] 2.5 Program.cs에 UserService 의존성 주입 등록
    - IUserService를 Scoped 서비스로 등록
    - _요구사항: 2.1_

- [x] 3. ChatHub 확장
  - [x] 3.1 LoginUser 메서드 추가
    - UserService를 통해 사용자 로그인/생성 처리
    - 성공 시 OnLoginSuccess 이벤트 전송 (userId, nickname, statistics)
    - 실패 시 OnLoginFailed 이벤트 전송
    - _요구사항: 1.4, 5.1, 6.1, 6.2, 6.3_

  - [x] 3.2 SelectSkin 메서드 추가
    - UserService를 통해 스킨 선택 저장
    - 성공 시 OnSkinSelected 이벤트 전송
    - 실패 시 OnSkinSelectionFailed 이벤트 전송
    - _요구사항: 8.3, 8.4_

- [x] 4. Login.razor 컴포넌트 구현
  - [x] 4.1 기본 컴포넌트 구조 및 라우팅 설정
    - @page "/login" 라우트 설정
    - 닉네임 입력 필드 UI 구현
    - 로그인 버튼 UI 구현
    - _요구사항: 1.1, 1.2, 3.1, 3.4_

  - [x] 4.2 입력 검증 로직 구현
    - 닉네임 길이 검증 (2-20자)
    - 공백 검증
    - 검증 실패 시 에러 메시지 표시
    - 검증 실패 시 로그인 버튼 비활성화
    - _요구사항: 1.3, 4.1, 4.2, 4.3, 4.4, 4.5_

  - [x] 4.3 연결 상태 표시 UI 구현
    - 연결 대기, 연결 중, 연결됨, 연결 실패 상태 표시
    - 상태별 시각적 피드백 제공
    - _요구사항: 2.1, 2.2, 2.3, 2.4, 2.5_

  - [x] 4.4 JavaScript Interop 설정
    - IJSRuntime 주입
    - SignalR 초기화 및 연결 메서드 호출
    - Hub 이벤트 수신을 위한 콜백 메서드 구현
    - _요구사항: 1.4, 3.5_

  - [x] 4.5 로그인 성공 시 로비로 리디렉션
    - NavigationManager를 사용하여 /lobby로 이동
    - 쿼리 파라미터로 userId와 nickname 전달
    - _요구사항: 7.1, 7.2, 7.3_

  - [x] 4.6 재시도 기능 구현
    - 연결 실패 시 재시도 버튼 표시
    - 페이지 새로고침 없이 재연결 시도
    - _요구사항: 7.4, 7.5_

- [x] 5. Lobby.razor 컴포넌트 구현
  - [x] 5.1 기본 컴포넌트 구조 및 라우팅 설정
    - @page "/lobby" 라우트 설정
    - 쿼리 파라미터에서 userId와 nickname 수신
    - _요구사항: 7.1, 7.2, 7.3_

  - [x] 5.2 사용자 정보 표시 UI 구현
    - 닉네임 표시
    - 사용자 ID 표시
    - _요구사항: 7.2, 7.3_

  - [x] 5.3 게임 통계 표시 UI 구현
    - 승/패/무승부 횟수 표시
    - 가위/바위/보 선택 통계 표시
    - 승률 계산 및 표시
    - _요구사항: 6.2, 6.3, 6.4_

  - [x] 5.4 손모양 스킨 선택 UI 구현
    - 2가지 스킨 옵션 표시
    - 각 스킨의 미리보기 이미지 표시
    - 선택된 스킨 시각적 강조
    - _요구사항: 8.1, 8.2, 8.3_

  - [x] 5.5 스킨 선택 처리 로직 구현
    - JavaScript Interop을 통해 SelectSkin Hub 메서드 호출
    - 선택 성공 시 게임 시작 버튼 활성화
    - _요구사항: 8.4, 8.5_

- [x] 6. JavaScript 클라이언트 파일 작성
  - [x] 6.1 login.js 파일 생성
    - SignalR 연결 초기화 함수
    - connectToHub 함수 구현
    - loginUser 함수 구현
    - OnLoginSuccess, OnLoginFailed 이벤트 핸들러 등록
    - _요구사항: 1.4, 3.5_

  - [x] 6.2 lobby.js 파일 생성
    - selectSkin 함수 구현
    - OnSkinSelected, OnSkinSelectionFailed 이벤트 핸들러 등록
    - _요구사항: 8.3, 8.4_

- [x] 7. Blazor 설정 및 통합
  - [x] 7.1 Program.cs에 Blazor Server 서비스 추가
    - AddServerSideBlazor 서비스 등록
    - MapBlazorHub 엔드포인트 추가
    - _요구사항: 3.2, 3.3_

  - [x] 7.2 _Layout.cshtml에 Blazor 스크립트 추가
    - blazor.server.js 스크립트 참조 추가
    - _요구사항: 3.2, 3.3_

  - [x] 7.3 _ViewImports.cshtml 업데이트
    - Blazor 컴포넌트를 위한 using 문 추가
    - _요구사항: 3.2_

- [x] 8. 스킨 이미지 리소스 추가
  - [x] 8.1 wwwroot/images/skins 디렉토리 생성
    - 스킨 1 이미지 파일 추가 (skin1.png)
    - 스킨 2 이미지 파일 추가 (skin2.png)
    - _요구사항: 8.2_

- [x] 9. 에러 처리 및 로깅 강화
  - [x] 9.1 UserService에 에러 처리 추가
    - Redis 연결 실패 처리
    - 데이터 저장/조회 실패 처리
    - 적절한 예외 메시지 및 로깅
    - _요구사항: 6.5_

  - [x] 9.2 ChatHub에 에러 처리 추가
    - 모든 Hub 메서드에 try-catch 블록
    - 클라이언트 친화적인 에러 메시지 전송
    - _요구사항: 2.3, 7.4_

- [x] 10. 스타일링 및 UI 개선
  - [x] 10.1 Login 페이지 CSS 작성
    - 로그인 폼 스타일링
    - 연결 상태 표시기 스타일링
    - 반응형 디자인 적용
    - _요구사항: 1.1, 2.1_

  - [x] 10.2 Lobby 페이지 CSS 작성
    - 통계 표시 레이아웃
    - 스킨 선택 UI 스타일링
    - 반응형 디자인 적용
    - _요구사항: 6.4, 8.1, 8.2_
