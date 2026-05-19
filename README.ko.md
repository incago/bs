# BetterScriptable

[English guide](README.md)

BetterScriptable은 Unity에서 ScriptableObject 기반 게임 데이터를 더 쉽게 확인하고, 비교하고, 수정하기 위한 유틸리티 에셋입니다.

ScriptableObject는 Unity에서 기본으로 지원되고 작은 데이터에는 편리하지만, 기본 Inspector만으로는 많은 데이터를 한눈에 비교하거나 여러 행을 빠르게 수정하기 어렵습니다. BetterScriptable은 런타임에서는 ScriptableObject를 그대로 사용하면서, 배열 데이터는 스프레드시트처럼 편집할 수 있게 해줍니다.

BetterScriptable은 두 파일을 한 쌍으로 사용합니다.

- `*.betterscriptable`: 원본 편집 문서입니다. 직렬화된 데이터, 수식, 디자인 전용 셀, 시트 메타데이터를 저장합니다.
- `*.asset`: 런타임에서 사용하는 ScriptableObject 산출물입니다. 게임 시스템은 이 파일을 읽으면 됩니다.

BetterScriptable Editor에서 `Save & Export`를 누르면 원본 문서를 저장하고 연결된 런타임 asset을 갱신합니다.

## 스크린샷

![BetterScriptable editor screenshot](screenshot01.png)

![BetterScriptable spreadsheet screenshot](screenshot02.png)

## 저장소 구조

```text
BetterScriptable Unity project
├── Assets/
│   ├── BetterScriptable/
│   └── Resources/
├── Packages/
│   └── com.rewuio.betterscriptable/
│       ├── Runtime/
│       ├── Editor/
│       ├── Tests/
│       ├── Documentation~/
│       └── Samples~/
├── ProjectSettings/
└── AGENTS.md
```

## 개발 정보

- Unity 버전: Unity 6.4 (`6000.4.5f1`)
- 메인 패키지: `Packages/com.rewuio.betterscriptable`
- Runtime namespace: `BetterScriptable`
- Editor namespace: `BetterScriptable.Editor`
- 재사용 가능한 코드는 가능하면 `Assets/`가 아니라 패키지 내부에 둡니다.

## 주요 에디터 도구

- `Tools > BetterScriptable > Generator`: ScriptableObject 데이터 클래스와 Editor 전용 factory 코드를 생성합니다.
- `Tools > BetterScriptable > Open`: BetterScriptable Editor 창을 엽니다.
- `Assets/Open in BetterScriptable Editor`: Project 창에서 선택한 `.betterscriptable` 파일을 엽니다.
- `Assets/BetterScriptable/Recreate Document From Asset`: 기존 ScriptableObject asset으로부터 BetterScriptable 문서와 factory를 생성합니다.

## 사용 방법 1: 처음부터 BetterScriptable을 사용하는 경우

새로운 데이터 타입을 만들고, ScriptableObject 클래스도 BetterScriptable이 생성해주기를 원하는 경우 이 흐름을 사용합니다.

1. `Tools > BetterScriptable > Generator`를 엽니다.
2. `ItemDataAsset`, `CharacterDataAsset` 같은 asset 클래스 이름을 입력합니다.
3. `BetterScriptable/game_data_item` 같은 생성 메뉴 경로를 설정합니다. 이 값은 `Assets/Create/...` 메뉴가 됩니다.
4. `ItemCategory`, `CharacterType`처럼 asset에 직접 들어가는 단일 필드를 추가합니다.
5. 테이블처럼 편집할 배열 데이터를 추가합니다.
   - `Row Type Name`: `ItemData` 같은 직렬화 row 클래스 이름입니다.
   - `Field Name`: `ItemDatas` 같은 asset 내부 배열 필드 이름입니다.
   - Data fields: `Id`, `Weight`, `RunSpeed`, `ResourceKey` 같은 컬럼입니다.
6. BSE 스프레드시트에서는 보이지만 런타임 `.asset`에는 export하지 않을 값은 디자인 필드로 표시합니다.
7. Generator의 위/아래 버튼으로 데이터 필드 순서를 조정합니다. 이 순서가 BSE의 컬럼 순서가 됩니다.
8. `Generate`를 누릅니다.
9. Unity가 생성된 스크립트를 컴파일할 때까지 기다립니다.
10. 생성된 `Assets/Create/...` 메뉴를 사용해서 `.betterscriptable` 문서와 `.asset` 쌍을 만듭니다.
11. `.betterscriptable` 파일을 선택하거나 `Tools > BetterScriptable > Open`으로 엽니다.
12. BetterScriptable Editor에서 asset fields, formulas, array rows를 수정합니다.
13. `Save & Export`를 누릅니다.

export 후에도 `.betterscriptable` 파일이 원본 데이터입니다. 연결된 `.asset`은 런타임 산출물입니다.

## 사용 방법 2: 기존 ScriptableObject로부터 Recreate해서 사용하는 경우

이미 프로젝트에 ScriptableObject 클래스와 `.asset` 데이터가 있고, 앞으로 BetterScriptable로 관리하고 싶은 경우 이 흐름을 사용합니다.

1. Unity Project 창에서 기존 `.asset` 파일을 선택합니다.
2. 우클릭 후 `BetterScriptable > Recreate Document From Asset`을 선택합니다.
3. BetterScriptable이 선택된 ScriptableObject 타입과 직렬화된 데이터를 읽습니다.
4. 선택한 `.asset` 옆에 `.betterscriptable` 문서가 생성됩니다.
5. 기존에 사용할 수 있는 생성 schema가 없다면, 런타임 스크립트 옆 `Editor` 폴더 아래에 factory 스크립트를 생성합니다.
6. 생성된 문서가 BetterScriptable Editor로 열립니다.
7. 추론된 필드와 배열 테이블이 의도와 맞는지 확인합니다.
8. BSE에서 데이터를 수정하고 필요할 때 `Save & Export`를 누릅니다.

Recreate 흐름은 마이그레이션을 위한 기능입니다. 기존 `.asset`은 연결된 런타임 산출물로 유지하고, 누락된 BetterScriptable 원본 문서를 새로 만듭니다.

schema는 직렬화 가능한 필드를 기준으로 추론합니다. 일반 직렬화 필드는 asset field가 되고, 직렬화 가능한 row 타입의 배열이나 list 필드는 스프레드시트 테이블이 됩니다. Recreate 후 디자인 필드, 컬럼 이름, 생성 메뉴 경로를 더 세밀하게 조정하고 싶다면 생성된 factory/schema를 수정하면 됩니다.

## BSE에서 편집하기

BetterScriptable Editor는 크게 세 영역으로 나뉩니다.

- `Asset Fields`: 배열이 아닌 직렬화 필드를 Unity Inspector처럼 보여줍니다.
- `Formulas`: 현재 선택한 배열 테이블에 대한 수식을 보여줍니다.
- `Array Table`: 선택한 배열 필드를 스프레드시트 형태로 보여줍니다.

asset에 배열 필드가 여러 개 있으면 `Formulas` 위의 탭으로 현재 편집할 테이블을 선택합니다.

Array Table 동작:

- 컬럼은 `A`, `B`, `C`로 시작하고 `AA`, `AB`, `AC`처럼 이어집니다.
- 행 번호는 `1`부터 시작합니다.
- 행 번호 영역과 컬럼 헤더는 스크롤 중에도 보이도록 고정됩니다.
- 일반 마우스 휠은 세로 스크롤입니다.
- `Shift + 마우스 휠`은 가로 스크롤입니다.
- `Tab`을 누르면 같은 행의 다음 셀로 이동합니다.
- `Enter`를 누르면 같은 컬럼의 다음 행으로 이동합니다.

## 수식

수식은 전체 컬럼 또는 특정 셀을 대상으로 입력할 수 있습니다.

예시:

```text
C = A + B
C1 = A1 + B1 + 300
D = C + '_key'
D = 'cookie' + FORMAT(A, '0000')
```

컬럼 수식은 해당 컬럼의 모든 행에 적용됩니다. 셀 수식은 컬럼 수식보다 우선순위가 높습니다. 예를 들어 `C = A + B`가 있어도 `C1 = A1 + B1 + 300`이 있으면 1번 행의 C 셀에는 셀 수식이 적용됩니다.

수식 결과는 `.betterscriptable` 원본 문서에 저장되고, `Save & Export`를 누르면 연결된 `.asset`에 export됩니다.

## 생성된 schema 수정하기

BetterScriptable로 생성했던 클래스를 다시 수정해야 하는 경우:

1. `Tools > BetterScriptable > Generator`를 엽니다.
2. Project 창에서 생성된 asset script 또는 factory script를 선택합니다.
3. Generator가 이전 schema 설정을 다시 불러옵니다.
4. 필드, 데이터 테이블, 디자인 필드 여부, 필드 순서를 수정합니다.
5. 다시 Generate합니다.

이미 만들어진 `.betterscriptable` 문서는 BSE에서 열 때 schema를 갱신하고, 새로 추가된 컬럼도 표시할 수 있습니다.
