---
target: simplePOS UI 전체 검토
total_score: 21
max_score: 40
na_heuristics: 
p0_count: 0
p1_count: 4
target_identity: "file:C:\\Users\\Admin\\orca\\simplePOS\\src\\Pos.App\\MainWindow.xaml"
target_fingerprint: "sha256:2d378c280794487078c2046d6df30152a0922a80d19b1edced4727d71ba5b6db"
target_path: "C:\\Users\\Admin\\orca\\simplePOS\\src\\Pos.App\\MainWindow.xaml"
timestamp: 2026-11-02T06-50-05Z
slug: src-pos-app-mainwindow-xaml
closed: true
---
Method: design review only (detector not applicable, native WPF platform)

# simplePOS UI critique

검토 대상: src/Pos.App/MainWindow.xaml 및 판매·상품 목록·상품 편집·설정 화면.
실제 WPF RenderTargetBitmap 미리보기 9개와 XAML/ViewModel 정적 검토. 빌드 성공, 경고 0 오류 0. 실제 터치, 스캐너, 스크린리더와 클릭 기반 동작 테스트는 수행하지 않음. 동작 관련 발견은 코드 경로 근거. 별도 SIMPLEPOS_HOME을 사용하여 검토 데이터를 분리함. 앱 소스 수정 없음.

## Design specificity and overall impression

판매 화면의 장바구니/빠른 추가/총액 구성은 POS 작업에 맞음. 관리 화면은 친숙한 표와 폼을 사용하지만 파랑/보라 액션 색과 시스템 기본 컨트롤이 혼재함. 외형보다 거래 연속성과 좁은 화면에서의 정보 보존이 가장 큰 기회. genesis-DESIGN.md는 커뮤니티 디자인 파일 플랫폼을 설명하므로 POS 제품 요구사항의 근거로 사용하지 않았음.

Deterministic scan: not applicable (native WPF XAML; web markup detector cannot validate this surface). Browser overlay: not applicable. Native rendered previews inspected instead.

## Heuristic scores

| 항목 | 점수 /4 | 근거 |
|---|---:|---|
| 상태 표시 | 2 | DB/백업 표시, 품절·처리 중 피드백 부족 |
| 현장 용어 | 3 | 장바구니·수량·판매 완료가 명확 |
| 사용자 제어 | 1 | 삭제/화면 이동 시 작업 복구 부족 |
| 일관성 | 2 | 판매 파랑, 관리 보라와 기본 컨트롤 혼재 |
| 실수 예방 | 2 | 판매 확인/검증 있음, 장바구니 손실 방지 부족 |
| 알아보기 쉬움 | 2 | 품절/필수/재고 공란 의미 불명확 |
| 작업 효율 | 2 | 스캐너/빠른 추가, 검색 판매/입력 전환 단절 |
| 시각적 절제 | 3 | 총액/판매 완료의 위계 명확 |
| 오류 복구 | 3 | 판매 실패 시 장바구니 유지/항목별 오류 |
| 도움말 | 1 | 스캔/재고 공란/설정 적용 범위 안내 부족 |
| Total | 21/40 | Acceptable, significant improvements needed |

## Strengths

- 총액과 판매 완료 버튼의 위계가 명확함.
- 대부분 48 DIP 이상인 조작 영역, 명시적인 하단 탐색, 공통 토큰과 포커스 스타일.
- 판매 저장 실패 시 장바구니 보존, 항목별 입력 검증, 재고 정책.

## Priority issues

1. [P1] 판매 중 작업 손실. SettingsViewModel SaveAsync/ReloadDatabaseAsync -> SettingsSaved -> ShellViewModel.ApplySettingsAsync -> BuildPagesAsync가 새 CartService를 생성함. MainPageViewModel.ClearCart와 RemoveItem은 즉시 삭제하며 되돌리기 없음. ProductEditor도 취소/탐색 전 미저장 경고 없음. 같은 DB 설정 변경에는 장바구니를 유지하고, DB 전환/미저장 이동은 명시적으로 안내하며 삭제 되돌리기를 제공. Suggested command: /impeccable harden.
   Evidence: src/Pos.App/ViewModels/ShellViewModel.cs:247,277; SettingsViewModel.cs:106,167; MainPageViewModel.cs:216,232; ProductEditorViewModel.cs:214.
2. [P1] 상품 찾기와 판매 흐름 단절. MainPage의 상품 찾기는 ProductListPage를 열지만 목록은 상품 추가/수정만 노출함. ProductListViewModel에도 장바구니 담기 명령이 없음. 빠른 추가에 없는 상품을 검색해서 담는 경로를 만들고 수량과 판매 화면 복귀를 연결. Suggested command: /impeccable shape.
   Evidence: src/Pos.App/Views/MainPage.xaml:202; Views/ProductListPage.xaml; ViewModels/ProductListViewModel.cs:153.
3. [P1] 900x640 레이아웃에서 정보 잘림. main-900.png에서 장바구니 약 한 줄과 빠른 추가 한 줄만 보임. products-900.png에서 초기화 버튼 잘림. 1200 미만에서 3:2 수직 분할, 필터 고정 폭이 원인. 장바구니 최소 가시 행 수를 확보하고 필터 줄바꿈/폭 대응. 수량 버튼의 MinWidth=56이 52 DIP 열보다 커 경계도 잘림. Suggested command: /impeccable adapt.
   Evidence: src/Pos.App/Views/MainPage.xaml.cs:8,23; MainPage.xaml:255; ProductListPage.xaml:63; Resources/Controls.xaml:175.
4. [P1] 자동 가상 키보드가 작업 영역을 덮음. 포커스로 열리는 키보드가 Grid.RowSpan=3인 오버레이로 검색 결과·다른 필드·하단 탐색을 가림. products-keyboard-900.png, editor-900.png, editor-keypad-900.png에서 확인. 터치 모드만 자동 열기, 물리 키보드 모드에서 선택적 열기, 활성 필드 위로 이동, 키패드에 현재 입력값/대상 표시, Esc 닫기와 포커스 복원 필요. Suggested command: /impeccable harden.
   Evidence: src/Pos.App/Views/ProductListPage.xaml.cs:15; ProductEditorPage.xaml.cs:15,20; ProductEditorPage.xaml:155; MainWindow.xaml.cs:36.
5. [P2] 품절 선표시 없음. 미리보기 재고 0인 컵라면이 일반 타일과 동일하게 보임. QuickAddItemViewModel.IsOutOfStock은 있지만 타일에서 바인딩하지 않음. 차단 정책에는 품절 표시, 허용 정책에는 재고 0/판매 허용을 명시. Suggested command: /impeccable clarify.
   Evidence: src/Pos.App/ViewModels/MainPageViewModel.cs:304; Views/MainPage.xaml:430.

## Cognitive load / emotional journey

중간 수준: 위계, 한 번에 한 작업, 점진적 노출에서 문제. 검색 영역은 텍스트/카테고리/상태/검색/초기화 다섯 선택이 노출됨. 빠른 추가 12개는 직접 선택용이므로 개수만으로 과부하라 단정하지 않음. 스캔/빠른 추가 시작은 수월하지만 검색-담기 단절, 자동 키보드, 설정 후 장바구니 손실에서 흐름이 끊김. 판매 확인 뒤 성공 안내까지 모달이 두 번 있어 반복 판매 효율 개선 여지. 실패 시 장바구니 유지 안내는 안심 요소.

## Persona red flags

- Alex, 숙련 직원: 검색 후 담기 없음, 가상 키보드 자동 열림, 판매마다 확인+성공 모달, 전용 작업 단축키 없음.
- Jordan, 신규 직원: 재고 빈칸이 재고 미관리라는 설명 없음, 필수 표시 부족, 품절 타일 구분 없음, 미등록 바코드가 안내 없이 편집으로 이동.
- Sam, 키보드/보조기기 사용자: 필드 TextBlock 라벨과 입력의 LabeledBy/명시적 접근성 이름 연결이 없음. 키보드 오버레이 Esc/포커스 관리 구현 없음. 스크린리더 실측은 미실시.

## Minor observations

- 색상 토큰 산술 계산: #6366F1/white 4.47:1, #EF4444/white 3.76:1, #006DFF/white 4.53:1. 일반 크기 버튼과 13 DIP 오류 문구 색상 검토 필요. 렌더링 픽셀 기반 측정은 아님.
- 영업중은 고정 텍스트이며 DB 연결 실패와 연동하지 않음.
- 검색 0건과 빠른 추가 0건에 맥락별 다음 행동 안내 부족.
- 폼 필수 표시, 가격 단위, 재고 공란 의미 안내 필요. 오류 문구는 있지만 Validation.HasError 스타일을 구동할 INotifyDataErrorInfo 연결은 확인되지 않음.
- 새로 등록한 상품 편집 완료 시 저장됨 상태가 화면 이동으로 즉시 사라질 수 있음.

## Design questions to consider

- 상품 찾기를 판매용 선택기로 만들면 빠른 추가에 없는 상품도 같은 흐름으로 계산할 수 있는가?
- 판매 중 설정 변경에도 장바구니를 유지하고 삭제를 되돌릴 수 있게 만들 수 있는가?
- 최소 크기에서 장바구니와 빠른 추가를 모두 상시 노출해야 하는가, 전환 구조가 더 효율적인가?

## Run notes

Screenshots: outputs/impeccable-review/main-1280.png, main-900.png, products-1280.png, products-900.png, editor-form-1280.png, editor-900.png, settings-900.png, products-keyboard-900.png, editor-keypad-900.png.
No server launched, each preview process exited. SDK installed locally into ignored .dotnet-sdk; preview data in ignored outputs. UI source unchanged. First run for this target.
