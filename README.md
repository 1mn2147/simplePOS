# Simple POS

Windows x64용 경량 POS 앱이다. 상품과 판매 데이터는 Excel `.xlsx`에 저장하며 Excel이나 .NET을 별도로 설치하지 않아도 동작한다.

## 다운로드

[Simple POS v1.0.0](https://github.com/1mn2147/simplePOS/releases/tag/v1.0.0)에서 `SimplePOS-1.0.0-win-x64.exe`와 SHA-256 체크섬 파일을 내려받을 수 있다.

```powershell
Get-FileHash .\SimplePOS-1.0.0-win-x64.exe -Algorithm SHA256
```

현재 실행 파일은 코드 서명되지 않아 Windows SmartScreen에서 알 수 없는 게시자 경고가 표시될 수 있다.

## 현재 구현

- 바코드 스캔, 미등록 바코드 상품 추가 연결, 4열 빠른 추가
- 장바구니 수량 조절, 상품 수정/제거, 비우기, 판매 완료
- 판매 완료 시 재고 차감과 `Sales`/`SaleItems` 이력의 원자 저장
- 품절 시 차단, 경고 후 허용, 경고 없는 그냥 허용 정책
- 상품 검색, 추가/수정, 카테고리, 공급사, 재고, 활성 상태
- 두벌식 가상 한글 키보드와 숫자 키패드
- Excel 원본 보존 마이그레이션과 외부 변경 감지
- 날짜 기반 주기/수동 백업, SHA-256 및 Excel 재열기 검증, 보존 개수
- Windows 시작프로그램 등록/해제와 중복 실행 방지
- `win-x64` self-contained 단일 EXE 게시

## 실행

개발 빌드의 게시 파일은 `artifacts/publish/win-x64/SimplePOS.exe`다. 첫 실행 시 `%LocalAppData%\SimplePOS` 아래에 설정, 기본 Excel DB, 로그를 만든다. 기존 Excel은 설정 화면의 `파일 선택`으로 지정하며, 구형 3열 파일은 원본을 유지한 채 `_pos.xlsx` 복사본으로 변환한다.

## 개발

.NET SDK 10.0.401 이상을 설치한다. `global.json`에 사용할 SDK 버전이 고정되어 있다.

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PWD '.nuget\packages'
dotnet restore SimplePOS.sln
dotnet build SimplePOS.sln --configuration Release
dotnet test tests\Pos.Tests\Pos.Tests.csproj --configuration Release
```

단일 EXE 게시:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

## 데이터 안전

상품 저장과 판매 완료는 원본 통합 문서를 읽은 뒤 임시 파일을 재검증하고 원자 교체한다. 관리하지 않는 시트, 수식, 추가 열은 보존한다. DB가 외부에서 바뀌거나 Excel에 잠겨 있으면 덮어쓰지 않고 실패한다. 백업은 원본과 크기·SHA-256이 같고 다시 열리는 경우에만 성공으로 처리한다.
