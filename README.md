<h1 align="center">Sephiria Build Overlay</h1>

<p align="center">Wiki 빌드 안내를 세피리아 게임 화면에 표시하는 모드</p>

<p align="center">
  <a href="https://github.com/westernbear/SephiriaBuildOverlay/releases/latest"><img src="https://img.shields.io/github/v/release/westernbear/SephiriaBuildOverlay?style=flat-square&color=86c9b6" alt="최신 릴리즈"></a>
  <a href="https://github.com/westernbear/SephiriaBuildOverlay/actions/workflows/ci.yml"><img src="https://github.com/westernbear/SephiriaBuildOverlay/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
</p>

<p align="center"><a href="https://github.com/westernbear/SephiriaBuildOverlay/releases/latest">다운로드</a> · <a href="#설치">설치</a> · <a href="#조작키">조작키</a></p>

## 설치

1. 게임을 종료하고 [최신 릴리즈](https://github.com/westernbear/SephiriaBuildOverlay/releases/latest)에서 `SephiriaBuildOverlay-{버전}.zip`을 받으세요. `Source code`는 설치 파일이 아닙니다.
2. ZIP을 풀고 내용물을 `Sephiria.exe`가 있는 폴더에 붙여넣으세요. BepInEx 5 x64가 포함되어 있어 별도로 설치하지 않아도 됩니다.
3. 게임을 실행한 뒤 `F9`를 눌러 Wiki 빌드 링크를 붙여넣고 **불러오기**를 누르세요.

기본 Steam 설치 경로는 `C:\Program Files (x86)\Steam\steamapps\common\Sephiria`입니다.

BepInEx가 이미 설치되어 있다면 `doorstop_config.ini`와 `BepInEx/config`를 먼저 백업하세요. 기존 로더 설정을 유지하려면 `doorstop_config.ini`는 덮어쓰지 마세요. BepInEx 6이나 다른 아키텍처의 로더와 섞어 설치하지 마세요.

## 조작키

| 기능 | 키보드·마우스 | 패드 |
| --- | --- | --- |
| 빌드 창 열기·닫기 | `F9` | View/Back 누른 채 방향키 ↑ |
| 오버레이 켜기·끄기 | `F7` | View/Back 누른 채 방향키 ← |
| 추천 선택·구매·리롤 한 번 실행 | `F8` | View/Back 누른 채 방향키 → |
| 인벤토리 전체 자동배치 시작·중단 | `F8` | View/Back 누른 채 방향키 → |
| 석판 수동 회전 | 석판을 가리키고 `Shift` + `F8` | 게임 기본 회전 조작 |
| 패널 버튼 이동 | 마우스 | 방향키 |
| 패널 버튼 누르기 | 클릭 | A / × |
| 패널 닫기 | `Esc` | B / ○ |
| 빌드 링크 붙여넣기 | 입력칸에서 `Ctrl` + `V` | 링크 복사 후 **클립보드 불러오기** |
| 패널 크기 조절 | 오른쪽 아래 모서리 드래그 | — |

자동배치는 인벤토리에서 한 번 누르면 이동·교환·회전까지 이어서 진행합니다. 다시 누르거나 창을 닫으면 중단됩니다. F8로 아티팩트를 선택해도 획득 후 인벤토리를 열어 자동배치를 진행합니다. 빌드 창은 닫고 오버레이는 켜 두세요.

사파이어 구매·재입고와 석판 합성은 추천만 표시합니다. 주사위 변환은 F8로 게임 확인 창을 열며, 변환은 그 창에서 승인하세요.

패드 조작은 게임이 패드 입력 모드일 때 작동합니다. View/Back을 누른 채 방향키를 한 번씩 눌러주세요.

단축키는 `BepInEx/config/io.github.sephiria.build-overlay.cfg`의 `[Keys]`에서 바꿉니다. 패드 보조 버튼은 `[Gamepad]`의 `Modifier`에서 `selectButton`(기본), `leftStickButton`, `rightStickButton` 중 하나로 설정하세요.
