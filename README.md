# Simple POS

Windows x64용 경량 POS 앱이다. 상품과 판매 데이터는 Excel `.xlsx`에 저장하며 Excel이나 .NET을 별도로 설치하지 않아도 동작한다.

## 다운로드

[Simple POS v1.1.1](https://github.com/1mn2147/simplePOS/releases/tag/v1.1.1)에서 `SimplePOS-1.1.1-win-x64.exe`와 SHA-256 체크섬 파일을 내려받을 수 있다.

```powershell
Get-FileHash .\SimplePOS-1.1.1-win-x64.exe -Algorithm SHA256
```

현재 실행 파일은 코드 서명되지 않아 Windows SmartScreen에서 알 수 없는 게시자 경고가 표시될 수 있다.

## Windows 버전 기능

- 바코드 스캔, 미등록 바코드 상품 추가 연결, 4열 빠른 추가
- 장바구니 수량 조절, 상품 수정/제거, 비우기, 판매 완료
- 상품 검색 후 수량을 지정해 장바구니 담기, 마지막 삭제/비우기 되돌리기 (`Ctrl+Z`)
- 같은 Excel DB의 설정 저장/다시 불러오기 시 장바구니 유지, 판매 중 DB 전환 방지
- 판매 완료 시 재고 차감과 `Sales`/`SaleItems` 이력의 원자 저장
- 품절 시 차단, 경고 후 허용, 경고 없는 그냥 허용 정책
- 상품 검색, 추가/수정, 카테고리, 공급사, 재고, 활성 상태
- 두벌식 가상 한글 키보드와 숫자 키패드
- 입력 옆 `한글`/`숫자` 버튼으로 키보드 열기, `Esc`로 닫기, 검색 상품 담기는 `Ctrl+Enter`
- 판매 화면에서 `Ctrl+Enter`로 판매 확인, 저장 성공은 팝업 대신 8초간 화면 안내로 표시
- 상품·설정·수량 입력 오류의 필드 강조와 첫 오류 포커스, 수정 중 오류 갱신
- 실제 장바구니 추가 성공에만 스캔 성공음, 최근 추가 상품·수량 안내와 해당 행 자동 표시
- 상품 등록·수정 후 복귀 화면의 완료 안내, 첫 상품 등록과 검색 결과 없음의 다음 행동 안내
- 설정 수정 중 표시와 미저장 이동 확인, 현재 DB만 새로고침할 때 수정 중인 설정 유지
- 좁은 창의 관리 폼 1열 배치, 긴 상품명·큰 금액 대응, 공통 입력·선택·키보드 포커스 스타일
- 한글 키보드 Shift 상태·변환 문자 표시와 카테고리 입력 지원
- Excel 원본 보존 마이그레이션과 외부 변경 감지
- 날짜 기반 주기/수동 백업, SHA-256 및 Excel 재열기 검증, 보존 개수
- Windows 시작프로그램 등록/해제와 중복 실행 방지
- `win-x64` self-contained 단일 EXE 게시

## 실행

개발 빌드의 게시 파일은 `artifacts/publish/win-x64/SimplePOS.exe`다. 첫 실행 시 `%LocalAppData%\SimplePOS` 아래에 설정, 기본 Excel DB, 로그를 만든다. 기존 Excel은 설정 화면의 `파일 선택`으로 지정하며, 구형 3열 파일은 원본을 유지한 채 `_pos.xlsx` 복사본으로 변환한다.

### macOS 개발 버전

저장소 루트에서 .NET SDK로 실행한다.

```sh
dotnet run --project src/Pos.Desktop
```

첫 실행 시 기본 데이터 디렉터리에 빈 `data/products.xlsx`를 만든다. 판매 화면의 **상품 관리**에서 첫 상품의 이름, 가격, 바코드를 등록한다. 상품을 스캔하거나 검색 결과에서 수량을 지정해 담은 뒤 판매를 확정하면 같은 파일에 재고와 판매 이력이 저장된다. 한글과 숫자 입력 버튼은 화면 키보드를 연다.

**설정**에서 기존 SimplePOS DB를 선택하거나 구형 Excel 상품 목록을 별도 파일로 변환할 수 있다. 장바구니가 비어 있으면 다른 DB로 전환할 수 있다. 같은 화면에서 백업 위치와 주기, 수동 백업, 재고 부족 처리, 스캔 성공음, 로그인 시 자동 실행을 설정한다. 예약 백업은 앱이 실행 중일 때 진행된다.

시작할 때 특정 DB를 열려면 `dotnet run --project src/Pos.Desktop -- "/path/to/products.xlsx"`로 실행한다. 인수를 생략하면 마지막으로 설정에 저장한 DB를 연다. 개발·테스트 데이터를 분리할 때는 `SIMPLEPOS_HOME`에 별도의 디렉터리를 지정한다. macOS 화면과 서비스 테스트는 `dotnet test tests/Pos.Desktop.Tests -c Release`로 실행한다.

## 개발

.NET SDK 10.0.401 이상을 설치한다. `global.json`에 사용할 SDK 버전이 고정되어 있다.

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PWD '.nuget\packages'
dotnet restore SimplePOS.sln
dotnet build SimplePOS.sln --configuration Release
dotnet test tests\Pos.Tests\Pos.Tests.csproj --configuration Release
dotnet test tests\Pos.App.Tests\Pos.App.Tests.csproj --configuration Release
```

`Pos.App.Tests`는 Windows WPF 판매 흐름을 별도 임시 데이터로 검증한다. 기존 카탈로그 마이그레이션 테스트 1개는 `상품목록.xlsx` 샘플 파일이 필요하다.

macOS에서는 같은 SDK로 판매 로직과 Excel 저장소를 검증할 수 있다. 저장소 루트에서 다음 명령을 실행한다.

```sh
dotnet test tests/Pos.Tests/Pos.Tests.csproj --configuration Release
```

실제 카탈로그 샘플이 있으면 `outputs/` 아래에 `상품목록.xlsx`를 둔다. 샘플이 없는 체크아웃에서는 아래 명령으로 해당 테스트 한 건을 제외한다. 이 경우에도 임시로 생성한 한글 상품 목록으로 마이그레이션, 판매, 백업 및 재열기를 검증한다.

```sh
dotnet test tests/Pos.Tests/Pos.Tests.csproj --configuration Release \
  --filter 'FullyQualifiedName!~Migration_ReproducesCatalogQualityCounts_WithoutChangingSource'
```

화면을 변경할 때는 [DESIGN.md](DESIGN.md)의 POS 디자인 기준을 따른다. UI 테스트는 최소 창 크기, 긴 상품명·최대 금액, 가상 키보드, 스캔 결과, 설정 초안 보존, 네이티브 컨트롤의 자동화 동작을 포함한다. `SIMPLEPOS_QA_OUTPUT` 환경 변수에 폴더를 지정하면 주요 검증 화면을 PNG로 저장한다. 고배율 PNG는 렌더링 검증이며 실제 Windows 배율 변경이나 물리 터치·스캐너 검증을 대신하지 않는다.

단일 EXE 게시:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

## 데이터 안전

상품 저장과 판매 완료는 원본 통합 문서를 읽은 뒤 임시 파일을 재검증하고 원자 교체한다. 관리하지 않는 시트, 수식, 추가 열은 보존한다. DB가 외부에서 바뀌거나 Excel에 잠겨 있으면 덮어쓰지 않고 실패한다. 백업은 원본과 크기·SHA-256이 같고 다시 열리는 경우에만 성공으로 처리한다.
