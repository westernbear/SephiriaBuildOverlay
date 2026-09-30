# Sephiria Build Overlay

Sephiria Wiki 빌드를 게임 안에서 검토하고, 목표 아티팩트·무기·나무 뿌리 선택을 한 동작씩 확인하는 Windows/BepInEx 5 모드입니다. SephPlanner의 코드, DLL, 프리셋 형식에 의존하지 않습니다.

## 안전 원칙

- 모드가 실행하는 선택·구매·리롤·이동은 매번 `F8`을 새로 눌러야 합니다. 한 입력은 한 요청만 보냅니다.
- 실행 직전에 로컬 플레이어, 런, 후보, 재화, 공유 주사위와 화면 revision을 다시 검사합니다.
- 게임 데이터를 직접 바꾸지 않고 정상 버튼/상호작용/인벤토리 요청 경로를 사용합니다.
- 해당 행동의 결과가 관찰되기 전에는 다음 요청을 보내지 않습니다. 상태 변경, 요청 거절, 5초 타임아웃 시 중단합니다. 현재 확인은 관찰 기반이며 명시적인 서버 ACK 훅은 아닙니다.
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

- `main` 브랜치 push와 pull request마다 Windows에서 복원, Release 빌드, 자동 테스트와 배포 ZIP 생성을 실행합니다.
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
4. `F7`로 안내를 켜거나 끕니다. 실제 아이템에 필수=금색, 추천=하늘색 프레임을 표시하며 다음 행동은 민트색 이중 프레임과 작은 확인 키로 구분합니다. 목표 밖 아이템의 외형은 바꾸지 않습니다. 수량은 목표에 마우스를 올렸을 때만 표시하고 소비 비용/주사위 위험은 확인 전에 항상 표시합니다.
5. 제안된 행동 하나를 실행하려면 `F8`을 한 번 누릅니다.

기본 키는 BepInEx 설정 파일 `BepInEx\config\io.github.sephiria.build-overlay.cfg`에서 바꿀 수 있습니다. 버전 불일치는 같은 설정의 `AcceptVersionMismatch`를 명시적으로 켜야 활성화할 수 있습니다.

중간 런에서 활성화하면 현재 인벤토리 인스턴스를 최소 획득 횟수로 기록하고 `기록 불확실`로 표시합니다. 이후 보상 획득은 Harmony 관찰 지점에서 별도 횟수로 증가하며 제단 강화는 세지 않습니다. F6의 `획득횟수 보정` 탭에서 수동 보정할 수 있습니다. 명시적으로 활성화한 검토 결과는 재로딩/재시작 후 게임 바인딩을 다시 검증하여 복원합니다.

실제 인게임 글자는 게임이 사용하는 TextMeshPro `PIXEL_SMALL` 폰트 자산을 참조합니다. 폰트 파일을 복사하거나 배포하지 않습니다. F6 검토 창은 별도 IMGUI UI로, 사용할 수 있는 Galmuri 또는 시스템 한글 폰트를 사용합니다. 가이드 창을 상시 띄우지 않으며 프레임과 반투명 고스트는 마우스 입력을 가로채지 않습니다.

## 데이터와 네트워크

- 고정 API `https://www.sephiria.wiki/api/builds/{UUID}`만 호출하며 쿠키나 로그인 정보를 사용하지 않습니다.
- 요청 제한은 10초, 응답 제한은 2 MiB입니다.
- 성공 응답은 `%LOCALAPPDATA%\SephiriaBuildOverlay`에 원자적으로 저장됩니다. 오프라인 복구 시 캐시 사용 사실과 나이를 표시합니다.
- 목표 횟수는 중복 제거된 `artifact_values`가 아닌 `content[].items[]`에서 계산합니다.
- 내장 카탈로그는 아티팩트 276개, 무기 158개, 나무 뿌리 21개입니다. Wiki 테이블 ID는 게임 ID가 아니므로 `tools\GenerateCatalog.ps1` 결과를 그대로 배포하면 안 됩니다. 게임 1.0.33 메타데이터로 444개 ID를 연결했고, 미해결 11개는 실제 게임 ID와 충돌하지 않는 별도 키를 사용합니다. 연결된 ID라도 실행 시 전체 메타데이터 검증에 통과해야 자동 행동이 허용됩니다.

## 프로젝트 구조

- `SephiriaBuildOverlay.Core` (`netstandard2.1`): import/cache, review, catalog verification, run progress, recommendations, confirmed executor, deterministic tablet/artifact solvers.
- `SephiriaBuildOverlay.Plugin` (`netstandard2.1`): BepInEx config/GUI, local-player Unity snapshot, candidate outline, normal game request bridge, reward observation.
- `SephiriaBuildOverlay.Tests` (`net8.0`): URL/schema/cache, duplicate goals, review gate, progress, dice policy, action safety, placement solvers.

특성 배분, 의상, 콤보와 과일꼬치는 v1에서 체크리스트입니다. 영구 특성 포인트는 자동 소비하지 않습니다. 네이티브 게임 입력 자체는 차단하지 않습니다.

## 검증 범위

코어와 플러그인 Release 빌드, 카탈로그 무결성, 77개 자동 테스트가 통과했습니다. 실제 싱글플레이 런에서 가져오기/분류 게이트, 아이템 선택창 프레임과 비용 경고, 게임 폰트, F7 숨김/복원, 비실행 슬롯 고스트, 재로딩 후 승인된 빌드 복원을 확인했습니다. 예제 빌드 매핑 47개 중 46개가 검증됐고 희귀도가 다른 `ice_snow`는 자동 행동을 허용하지 않습니다.

석판 회전/조건부 효과/고정 각인 최적화는 순수 코어 테스트 범위입니다. 런타임은 현재 석판을 고정한 안전한 아이템 개선 배치와 빈 슬롯 이동 경로만 연결했습니다. 조건부 효과·점유 칸 교환·석판 회전은 수동입니다. 실제 F8 구매/리롤/이동, 지혜 10 병합의 실게임 관찰, 최종 배치 전체 시나리오는 미검증입니다. 상점의 보상이 아닌 획득 카운트와 보상 포기/주사위 변환 연결도 후속 작업입니다. 멀티플레이 실게임은 사용자 요청에 따라 검증 범위에서 제외했습니다.

디버깅 기록은 [docs/DEBUGGING.md](docs/DEBUGGING.md), 재시작 없는 개발 및 코드 기반 읽기 전용 검사는 [docs/FAST_TESTING.md](docs/FAST_TESTING.md)를 참고하세요.
