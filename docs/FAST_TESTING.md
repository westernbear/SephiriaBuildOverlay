# 빠른 게임 내부 검증

개발용 도구는 배포 ZIP에 넣지 않습니다. 일반 설치는 여전히 게임 루트에 ZIP을 풀기만 하면 됩니다. 아래 GameDir는 **개발용 배치/진단 대상**이며 소스 빌드에는 설치 경로가 필요하지 않습니다.

## 재시작 없이 Plugin 변경 확인

[공식 BepInEx.Debug ScriptEngine](https://github.com/BepInEx/BepInEx.Debug#scriptengine)은 `BepInEx/scripts`의 BaseUnityPlugin DLL을 재로딩합니다. 플러그인 종료 시 Harmony 패치, 장면 이벤트, 계산 작업, 생성한 GUI 리소스를 정리해야 합니다. 의존 DLL을 완전히 언로드하는 도구는 아닙니다.

게임을 종료한 상태에서 다음 개발용 구조로 설정합니다.

```text
BepInEx/plugins/ScriptEngine.dll
BepInEx/plugins/SephiriaBuildOverlay/SephiriaBuildOverlay.Core.dll
BepInEx/scripts/SephiriaBuildOverlay.Plugin.dll
BepInEx/scripts/SephiriaBuildOverlay.Plugin.pdb
BepInEx/config/com.bepis.bepinex.scriptengine.cfg
```

ScriptEngine은 [r11.1 공식 릴리스](https://github.com/BepInEx/BepInEx.Debug/releases/tag/r11.1)의 패키지를 사용했습니다. 설정 템플릿은 `tools/dev/ScriptEngine.cfg`입니다. 기본 F6 재로딩은 SephPlanner와 충돌할 수 있어 `ReloadKey = None`, 파일 감지와 2초 지연을 사용합니다. 이 모드의 가져오기 기본 키는 F9입니다. 같은 Plugin DLL을 plugins와 scripts에 동시에 두지 마세요.

```powershell
.\tools\dev\DeployPlugin.ps1
```

빌드 성공 후 PDB를 먼저, DLL을 나중에 복사합니다. 설치된 Core와 해시가 다르면 배치를 거절합니다. **Core 변경은 게임 종료 → Core 교체 → 재시작**이 필요합니다. DLL이 달라졌다고 모든 의존 코드가 바뀐 것으로 간주하면 안 됩니다.

검토 후 활성화한 빌드는 `review.json`에 저장합니다. 재로딩/재시작 후 게임 메타데이터를 다시 검증하고 같은 런의 획득 기록을 복원합니다. 미분류 상태의 가져오기를 임의 활성화하지 않습니다. 이 기능 추가 전에 유실된 분류는 복구할 수 없습니다.

## Computer Use 없이 실제 런 상태 검사

게임을 실행하지 않고 설치된 네이티브 프리셋 API/해금 경로/요청 큐의 계약을 검사하려면 다음을 실행합니다. 게임 DLL은 메타데이터로만 읽으며 코드를 실행하거나 저장 데이터를 변경하지 않습니다.

```powershell
.\tools\dev\TestStartingPresetContract.ps1
```

개발 중에만 `BepInEx/config/io.github.sephiria.build-overlay.cfg`에서 아래를 켭니다.

```ini
[Debug]
RuntimeDiagnostics = true
MeasurePerformance = true
```

```powershell
.\tools\dev\InvokeRuntimeProbe.ps1 -Command snapshot
.\tools\dev\InvokeRuntimeProbe.ps1 -Command catalog
.\tools\dev\TestRuntimeSmoke.ps1 -ExpectedBuild '<활성 빌드 UUID>'
.\tools\dev\InvokeRuntimeProbe.ps1 -Command preview
```

명령 파일은 `BepInEx/cache/SephiriaBuildOverlay/diagnostics`를 이용합니다. UUID별 JSON 응답이 남습니다. `snapshot`은 화면 후보, 돈/주사위, 로컬 소유권, 인벤토리 좌표와 활성 빌드 상태 및 수동 입력 비수신 Canvas/폰트를 제공합니다. `catalog`는 실제 엔티티의 메타데이터만 수집합니다. `TestRuntimeSmoke`는 연속 스냅샷의 자원/인벤토리 불변, 승인된 빌드 복원, 게임 폰트, 소비 경고를 검사합니다. 사용자가 그 사이 플레이하면 상태 변경으로 실패하므로 정지된 UI에서 실행합니다.

`preview`는 실제 아이템을 **옮기지 않고** 빈 슬롯에 10초 동안 `표시 검증 / 비실행` 고스트만 그립니다. 미리보기 동안 F8은 차단됩니다. 이는 렌더링 검증이며 솔버 결과가 아닙니다. 임의 C# 실행, 목표 함수 호출, 키 주입, 구매/리롤/이동 명령은 노출하지 않습니다. 진단을 실제 F8 행동 테스트와 혼동하지 마세요.

`InvokeRuntimeProbe.ps1 -Command controller-preview`는 10초 동안 입력이 비활성화된 패드 검토 창만 표시한 뒤 이전 창 표시 상태를 복원합니다. 게임 입력 모드나 장치를 바꾸지 않고 빌드를 수정하거나 행동을 실행하지 않습니다. 이는 패드 UI 렌더링 검사이지 실제 패드 조작 검증이 아닙니다. `snapshot`의 `overlay.input`에는 게임의 입력 모드, 연결된 로컬 패드 수/식별자와 모달 훅 설치 여부가 포함됩니다.

`ConfigureReviewedBuild.ps1`은 모든 구역의 역할을 명시적으로 전달받아 검토 체크포인트만 저장합니다. `-Activate`도 게임 선택이 아니라 다음 로드의 바인딩 재검증 후 빌드 활성화를 뜻합니다. 사용자의 분류 승인 없이 임의로 사용하지 마세요.

## 일반 배포 모드 복원

게임을 종료한 뒤 `RestoreProductionMode.ps1 -PackageDirectory '<dist 패키지 폴더>'`를 실행하면 두 자체 DLL을 정상 plugins 경로에 복사하고 scripts의 자체 DLL/PDB를 로컬 백업으로 이동합니다. 다른 스크립트 DLL이 없을 때만 개발용 ScriptEngine을 백업으로 이동합니다. 실행 중인 게임에는 이 작업을 거절합니다. 다른 모드, 저장 파일, 빌드 캐시는 삭제하지 않습니다.

성능 로그는 스냅샷 평균/p95/최대 시간과 **포커스 상태에서만** 측정한 FPS를 10초마다 기록합니다. 첫 초기화/재로딩 구간은 별도로 제외하고 같은 장면의 안정 구간끼리 비교합니다. 전체 MonoProfiler 활성화는 자체 비용이 커 기본으로 사용하지 않습니다.

## Computer Use 없는 정상 종료 검사

게임을 정상적으로 닫은 상태에서 실행합니다.

```powershell
./tools/dev/TestExitSmoke.ps1 -ExpectedVersion 0.1.6
```

Steam에 `--sbo-exit-smoke` 옵션을 전달하고 게임 내부 메인 스레드에서 15초 이후 열린 타이틀의 정상 `QuitGame` 함수만 호출합니다. 로컬 아바타가 있거나 요청 대기 중이면 호출하지 않습니다. 기존 런을 자동 진입/종료하거나 강제 종료하지 않으며, 시간 초과 시 게임을 그대로 둡니다. 일반 실행에는 옵션이 없어 아무 동작도 하지 않습니다. 프로세스 종료 코드, 새 덤프, Unity Crash 로그, 조기 UIA/모드 정리 로그를 검사합니다. `RuntimeDiagnostics`를 켤 필요는 없습니다. 활성 런/외부 UIA 클라이언트 연결 상태의 종료 검증과는 구분합니다.

## 외부 도구 참고

- [Surity](https://github.com/olavim/Surity)는 Unity 모드용 C# 및 IEnumerator 테스트 프레임워크입니다. `Surity.BepInEx`와 `Surity.CLI`를 이용하면 게임 내부 Unity API를 코드로 검사할 수 있습니다. CLI는 게임을 batchmode로 실행하고 결과를 받습니다. **Sephiria/Steam 재실행 경로에서의 호환성은 아직 검증하지 않았으므로**, 현재 통과 기록은 자체 읽기 전용 진단과 xUnit 결과만 사용합니다.
- [UnityExplorer C# Console](https://github.com/sinai-dev/UnityExplorer#c-console)은 게임 내부 코드를 호출할 수 있지만 범용 코드 실행이 가능하고 별도 런타임 호환성 확인이 필요합니다. 설치하지 않았습니다.
- [RuntimeUnityEditor](https://github.com/ManlyMarco/RuntimeUnityEditor#known-issues)는 .NET Standard 런타임에서 REPL 제한을 안내합니다. 이 환경의 기본 테스트 도구로 선택하지 않았습니다.
- [AltTester](https://alttester.com/docs/sdk/latest/pages/overview.html)는 코드 기반 Unity 자동화를 제공하지만 계측된 게임 빌드가 필요합니다. 원본 프로젝트가 없는 Steam 게임에 즉시 적용할 수 있다고 간주하지 않습니다.

실제 선택/구매/리롤/이동은 여전히 표시된 비용을 확인한 후 F8 한 번당 한 요청만 허용합니다. 영구 포인트와 사용자 저장 데이터는 테스트 명령에서 수정하지 않습니다.
