---
name: Simple POS
description: 한국어 Windows 매장에서 상품을 찾고 판매를 기록하는 작업 화면
colors:
  primary: "#006DFF"
  primary-hover: "#005AD6"
  primary-pressed: "#0049B0"
  primary-tint: "#EEF4FF"
  background: "#FAFAFA"
  surface: "#FFFFFF"
  surface-muted: "#F4F4F5"
  surface-alternate: "#F8FAFC"
  workspace: "#EFEFF4"
  text-primary: "#0A0A0A"
  text-secondary: "#6B6B6B"
  text-disabled: "#9C9C9C"
  border: "#E8E8EC"
  control-border: "#8A8F98"
  selection: "#E7F0FF"
  selection-text: "#102D55"
  header: "#1A283E"
  header-hover: "#243653"
  header-pressed: "#101D30"
  header-muted: "#B9C3D2"
  header-divider: "#40506A"
  brand-accent: "#F4D35E"
  navigation-background: "#101010"
  navigation-muted: "#AEB8C8"
  navigation-hover: "#202020"
  navigation-pressed: "#2A2A2A"
  navigation-selected: "#181818"
  success: "#10B981"
  success-tint: "#ECFDF5"
  warning: "#F59E0B"
  warning-tint: "#FFFBEB"
  error: "#B91C1C"
  error-tint: "#FEF2F2"
  error-hover: "#991B1B"
  error-pressed: "#7F1D1D"
  on-primary: "#FFFFFF"
typography:
  page-title:
    fontFamily: "Malgun Gothic, Segoe UI"
    fontSize: "28px"
    fontWeight: 700
  section-title:
    fontFamily: "Malgun Gothic, Segoe UI"
    fontSize: "20px"
    fontWeight: 600
  subhead:
    fontFamily: "Malgun Gothic, Segoe UI"
    fontSize: "17px"
    fontWeight: 600
  body:
    fontFamily: "Malgun Gothic, Segoe UI"
    fontSize: "15px"
    fontWeight: 400
  label:
    fontFamily: "Malgun Gothic, Segoe UI"
    fontSize: "13px"
    fontWeight: 600
  caption:
    fontFamily: "Malgun Gothic, Segoe UI"
    fontSize: "12px"
    fontWeight: 400
  amount:
    fontFamily: "Malgun Gothic, Segoe UI"
    fontSize: "28px"
    fontWeight: 700
rounded:
  small: "4px"
  control: "6px"
  panel: "8px"
spacing:
  xs: "4px"
  sm: "8px"
  field: "12px"
  control: "16px"
  panel: "20px"
  page: "24px"
  section: "32px"
  spacious: "40px"
  touch: "48px"
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-primary}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "0px 16px"
    height: "48px"
  button-primary-hover:
    backgroundColor: "{colors.primary-hover}"
  button-primary-pressed:
    backgroundColor: "{colors.primary-pressed}"
  button-large:
    typography: "{typography.subhead}"
    padding: "0px 24px"
    height: "56px"
  button-secondary:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-primary}"
    rounded: "{rounded.control}"
    height: "48px"
  button-danger:
    backgroundColor: "{colors.error}"
    textColor: "{colors.on-primary}"
    rounded: "{rounded.control}"
    height: "48px"
  input:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-primary}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "10px 14px"
    height: "48px"
  quick-add:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-primary}"
    rounded: "{rounded.control}"
    padding: "8px"
    height: "108px"
  navigation:
    backgroundColor: "{colors.navigation-background}"
    textColor: "{colors.navigation-muted}"
    height: "64px"
  data-row:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-primary}"
    typography: "{typography.body}"
    height: "56px"
---

# Design System: Simple POS

## Overview

**Creative North Star: "판매 흐름을 지켜 주는 계산대"**

반복해서 상품을 찾고, 수량과 금액을 확인하고, 판매를 기록하는 한국어 Windows 작업 화면이다. 밝은 상품·입력 영역과 어두운 탐색 영역을 구분하고 파랑으로 주요 동작과 현재 상태를 알린다. 매장 직원이 익숙한 표, 입력란, 버튼을 사용하며 장식보다 정보의 위치와 조작 결과를 명확하게 만든다.

이 문서는 현재 POS 구현을 기록한다. 원본 토큰은 `src/Pos.App/Resources/DesignTokens.xaml`, 공통 컨트롤은 `Resources/Controls.xaml`이다. `genesis-DESIGN.md`는 다른 제품의 참고 자료이며 이 앱의 디자인 규칙이 아니다. 위 스키마의 `px`는 100% 배율에서 WPF의 DIP와 같은 크기를 나타낸다. 버튼·입력의 높이는 최소 높이이고 빠른 추가 타일과 표 행은 고정 높이다.

**Key Characteristics:**

- 한국어 로컬 글꼴과 고정 역할별 크기.
- 밝은 작업 영역, 어두운 탐색, 파란 주요 동작.
- 터치와 키보드 모두에서 확인할 수 있는 선택·포커스.
- 금액, 수량, 상품명을 우선하는 읽기 순서.

## Colors

파랑은 판매·저장·담기 같은 주요 동작과 현재 선택에 사용한다. `brand-accent`는 기존 판매 헤더의 작은 식별 표시에 한정한다. 성공·주의의 밝은 색은 상태 점이나 배경에 사용하고, 그 위의 설명은 `text-primary`로 읽히게 한다. 오류 설명은 `error`로 표시한다.

밝은 작업 면은 `background`, `surface`, `workspace`로 구분한다. `border`는 패널과 표의 가벼운 구분선이며 입력란·체크박스처럼 경계 자체가 조작 위치를 알려 주는 요소는 `control-border`를 사용한다. 어두운 헤더와 탐색의 보조 문구에는 각각 `header-muted`, `navigation-muted`를 사용한다.

선택한 행은 `selection` 배경, `selection-text` 문자, 굵기 변화로 표시한다. 키보드 포커스는 별도의 파란 윤곽선으로 표시하여 선택과 이동 위치를 구별한다.

## Typography

제목과 본문은 모두 맑은 고딕을 먼저 사용하고 Segoe UI로 대체한다. 별도 웹 글꼴 다운로드가 없다. 숫자·시각 등 고정폭 표현이 필요한 제한된 위치의 `FontMono`는 Consolas와 맑은 고딕을 사용한다.

페이지 제목, 섹션 제목, 본문, 라벨의 역할은 공통 스타일로 구분한다. 크기만 키우기보다 굵기·여백·보조색으로 우선순위를 만든다. 금액과 숫자 표는 `Typography.NumeralAlignment=Tabular`를 적용하고 오른쪽 정렬한다. 사용 글꼴의 OpenType 지원 범위에 따라 숫자 폭의 효과가 달라질 수 있다.

길이가 일정하지 않은 상품명과 경로는 주요 조작을 밀어내지 않게 처리한다. 목록에서는 말줄임과 전체값 툴팁을 제공하고, 입력값은 키보드와 포커스로 끝까지 확인할 수 있어야 한다.

## Layout

창의 기본 크기는 1280×800, 최소 크기는 900×640 DIP다. 가용 폭에 따라 판매 목록·빠른 추가·결제 영역과 관리 폼을 재배치하며, 글자와 터치 목표를 일괄 축소하지 않는다. 빠른 추가는 1050 DIP 미만에서 2열, 1050 이상에서 3열, 1200 이상에서 4열이다. 1200 미만의 결제 영역은 하단으로 이동한다. 관리 폼은 1000 미만에서 1열로 재배치한다.

기본 간격은 4 DIP 배수다. 관련 필드와 버튼은 가깝게 묶고, 다른 작업 그룹 사이를 넓힌다. 페이지 여백과 패널 여백은 토큰을 사용한다. 상품 목록은 긴 상품명에 여유 폭을 먼저 주며 숫자 열은 허용되는 가격과 재고를 표현할 폭을 확보한다.

하단 탐색은 각 페이지에서 같은 순서를 유지한다. 가상 키보드는 입력 대상과 현재 값을 함께 보여 주고, 화면의 작업 영역과 함께 배치한다. 최소 크기와 125%·150% 배율에서 작업 버튼과 현재 입력을 확인한다.

## Elevation & Depth

공통 컨트롤에 장식용 그림자나 등장 애니메이션을 사용하지 않는다. 면의 명도 차이와 얇은 경계로 영역을 구분한다. ComboBox 목록은 Popup으로 컨테이너 밖에 표시되며 배경·경계로 본문과 구분한다.

## Shapes

버튼과 입력은 공통 control 반경, 패널은 panel 반경, 체크박스 표시는 small 반경을 사용한다. 기본 버튼은 최소 48×48 DIP, 주요 판매·저장 동작은 최소 높이 56 DIP다. 체크박스의 사각 표시는 20 DIP이며 텍스트를 포함한 전체 행이 최소 높이 48 DIP의 클릭 영역이다.

## Components

**버튼.** Primary는 판매·저장·담기, Secondary는 보조 동작, Ghost는 덜 강조하는 행 작업, Danger는 비우기처럼 복구에 주의가 필요한 동작에 사용한다. hover·pressed는 공통 토큰으로 구분한다. 비활성 버튼은 투명도로 구분하고 키보드 포커스는 3 DIP 윤곽선으로 드러낸다. 눌렀을 때 장식적으로 이동하지 않는다.

**입력.** TextBox와 ComboBox는 같은 글꼴·최소 높이·경계 규칙을 사용한다. 포커스는 파랑, 검증 오류는 빨강 경계와 필드 설명으로 표현한다. ComboBox의 선택·편집·팝업은 WPF 기본 컨트롤 동작과 필수 템플릿 부분을 유지한다. CheckBox는 기본 Toggle/Automation 동작을 유지하면서 사각 표시와 체크 경로를 그린다.

**상품 표.** 행 높이는 56 DIP이며 셀 내용은 세로 중앙에 놓는다. `DataGridTextStyle`은 일반 문자, `DataGridNumericTextStyle`은 금액·재고에 적용한다. `DataGridNumericColumnHeaderStyle`로 숫자 열 머리글도 맞춘다. 선택한 행과 키보드 포커스 셀을 동시에 구별할 수 있으며 행 가상화를 유지한다.

**탐색.** 어두운 배경에 네 개의 화면 이동 버튼을 같은 순서로 표시한다. 현재 화면은 위쪽 파란 선·밝은 문자·굵기로 나타내고 UI Automation의 `ItemStatus`에도 전달한다. hover·pressed에서도 현재 위치를 유지한다.

**장바구니.** 상품명·금액을 윗줄, 단가·수량·수정·제거를 아랫줄에 둔다. 최근 추가한 상품은 배경과 안내로 식별한다. 입력·추가·완료 동작 뒤에 상태 문구를 읽을 수 있어야 한다.

**빠른 추가.** 상품명, 품절 상태, 가격을 높이 108 DIP 타일에 배치한다. 타일 안쪽 여백은 8 DIP, 바깥 여백은 4 DIP다. 이름은 줄 높이 20 DIP로 최대 40 DIP의 두 줄 영역, 상태는 줄 높이 14 DIP로 최대 28 DIP의 영역을 사용한다. 가격은 내용에 맞춘 높이를 사용한다. 타일 전체가 동작 영역이며 상품명은 접근성 이름으로 전달한다. 목록이 비었을 때는 실제 등록 동작으로 연결하는 안내를 제공한다.

## Do's and Don'ts

- **Do** 공통 XAML 토큰과 스타일을 사용하고 같은 역할을 모든 페이지에서 같은 방식으로 표시한다.
- **Do** 긴 한국어 상품명, 최대 가격, 품절, 빈 목록, 키보드 입력 상태로 화면을 확인한다.
- **Do** 색뿐 아니라 문자·윤곽선·굵기로 상태와 현재 위치를 전달한다.
- **Don't** 판매 작업 영역을 확보하려고 글자나 터치 목표를 일괄 축소한다.
- **Don't** 로컬 페이지에 동일한 의미의 색·버튼 스타일을 다시 정의한다.
- **Don't** 저장 중·오류·복구 동작을 장식 효과로 대체한다.
