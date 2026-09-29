# Sephiria Build Overlay

Sephiria Wiki 빌드를 게임 안에서 검토하고, 목표 아티팩트·무기·나무 뿌리 선택을 한 동작씩 확인하는 Windows/BepInEx 5 모드입니다. SephPlanner의 코드, DLL, 프리셋 형식에 의존하지 않습니다.

## 안전 원칙

- 모드가 실행하는 선택·구매·리롤은 매번 `F8`을 새로 눌러야 합니다. 한 입력은 한 요청만 보냅니다.
- 실행 직전에 로컬 플레이어, 런, 후보, 재화, 공유 주사위와 화면 revision을 다시 검사합니다.
- 게임 데이터를 직접 바꾸지 않고 Unity의 정상 버튼 `onClick` 또는 게임 `Interactable.Interactive` 경로만 호출합니다.
- 서버 확인 전에는 다음 요청을 보내지 않습니다. 상태 변경, 요청 거절, 5초 타임아웃 시 중단합니다.
- 현재 게임 엔티티의 ID·한국어 이름·희귀도·카테고리 또는 무기 티어·부모가 내장 1.0.33 카탈로그와 다르면 해당 자동 행동만 비활성화합니다.
- 멀티플레이에서는 로컬 소유 플레이어의 인벤토리와 상태만 읽습니다.

## 설치

요구 사항은 Sephiria 1.0.33과 BepInEx 5입니다. [Releases](https://github.com/westernbear/SephiriaBuildOverlay/releases)에서 `SephiriaBuildOverlay-{버전}.zip`을 받은 뒤 **게임 루트 폴더에 그대로 압축 해제**합니다. ZIP 내부가 다음 구조이므로 별도로 DLL을 옮길 필요가 없습니다.

```text
Sephiria/
└─ BepInEx/
   └─ plugins/
      └─ SephiriaBuildOverlay/
         ├─ SephiriaBuildOverlay.Core.dll
         └─ SephiriaBuildOverlay.Plugin.dll
```

## 소스 빌드

.NET 8 SDK가 필요합니다. 공개 BepInEx/Unity 참조 패키지를 사용하므로 Sephiria 설치 경로나 게임 DLL은 빌드에 필요하지 않습니다. 게임 DLL과 에셋은 저장소와 배포물에 포함되지 않습니다.

```powershell
dotnet restore SephiriaBuildOverlay.sln
dotnet test SephiriaBuildOverlay.sln -c Release
.\tools\BuildRelease.ps1
```

스크립트는 바로 게임 루트에 붙여넣을 수 있는 `dist\SephiriaBuildOverlay-{버전}` 폴더와 같은 내용의 ZIP, SHA-256 파일을 만듭니다.

## CI와 릴리스

- `main` 브랜치 push와 pull request마다 Windows에서 복원, Release 빌드, 38개 테스트와 배포 ZIP 생성을 실행합니다.
- `v0.1.0` 같은 SemVer 태그를 push하면 테스트 및 패키징 후 ZIP과 SHA-256을 첨부한 GitHub Release가 자동 생성됩니다.

```powershell
git tag v0.1.0
git push origin v0.1.0
```

배포물에는 Core와 Plugin DLL만 포함됩니다. BepInEx, Harmony, Unity와 게임 DLL은 설치된 게임의 것을 사용합니다.

## 사용법

1. `F6`을 눌러 `https://(www.)sephiria.wiki/builds/{UUID}` 또는 원시 UUID를 붙여넣습니다.
2. 모든 자유 형식 구역을 `필수`, `추천`, `제외`로 분류합니다. 항목마다 역할, 횟수, 우선순위와 카탈로그 slug를 고칠 수 있습니다.
3. `검토 완료 및 활성화`를 누릅니다. 필수 항목이 해석되지 않으면 활성화되지 않습니다.
4. `F7`로 가이드를 켜거나 끕니다. 노란 테두리와 다음 행동, 비용, 공유 주사위 위험을 확인합니다.
5. 제안된 행동 하나를 실행하려면 `F8`을 한 번 누릅니다.

기본 키는 BepInEx 설정 파일 `BepInEx\config\io.github.sephiria.build-overlay.cfg`에서 바꿀 수 있습니다. 버전 불일치는 같은 설정의 `AcceptVersionMismatch`를 명시적으로 켜야 활성화할 수 있습니다.

중간 런에서 활성화하면 현재 인벤토리 인스턴스를 최소 획득 횟수로 기록하고 `기록 불확실`로 표시합니다. 이후 보상 획득은 Harmony 관찰 지점에서 별도 횟수로 증가하며 제단 강화는 세지 않습니다. 오버레이의 `-`/`+`로 수동 보정할 수 있습니다.

## 데이터와 네트워크

- 고정 API `https://www.sephiria.wiki/api/builds/{UUID}`만 호출하며 쿠키나 로그인 정보를 사용하지 않습니다.
- 요청 제한은 10초, 응답 제한은 2 MiB입니다.
- 성공 응답은 `%LOCALAPPDATA%\SephiriaBuildOverlay`에 원자적으로 저장됩니다. 오프라인 복구 시 캐시 사용 사실과 나이를 표시합니다.
- 목표 횟수는 중복 제거된 `artifact_values`가 아닌 `content[].items[]`에서 계산합니다.
- 내장 카탈로그는 아티팩트 258개, 무기 158개, 나무 뿌리 21개입니다. `tools\GenerateCatalog.ps1`로 동일한 형식을 재생성할 수 있습니다.

## 프로젝트 구조

- `SephiriaBuildOverlay.Core` (`netstandard2.1`): import/cache, review, catalog verification, run progress, recommendations, confirmed executor, deterministic tablet/artifact solvers.
- `SephiriaBuildOverlay.Plugin` (`netstandard2.1`): BepInEx config/GUI, local-player Unity snapshot, candidate outline, normal game request bridge, reward observation.
- `SephiriaBuildOverlay.Tests` (`net8.0`): URL/schema/cache, duplicate goals, review gate, progress, dice policy, action safety, placement solvers.

특성 배분, 의상, 콤보와 과일꼬치는 v1에서 체크리스트입니다. 영구 특성 포인트는 자동 소비하지 않습니다. 네이티브 게임 입력 자체는 차단하지 않습니다.

## 검증 범위

코어와 플러그인 Release 빌드, 카탈로그 무결성, 38개 자동 테스트를 실행했습니다. 석판 회전/조건부 효과/고정 각인과 아티팩트 최소 이동 계산은 순수 코어에서 검증됩니다. 실제 게임의 화면·필드 오브젝트는 1.0.33 타입과 멤버를 런타임에 다시 확인하며, 식별이 모호하면 제안만 표시하거나 자동 행동을 중지합니다. 싱글플레이 및 호스트/클라이언트의 최종 수동 승인 시나리오는 설치 후 별도 실행 검증이 필요합니다.
