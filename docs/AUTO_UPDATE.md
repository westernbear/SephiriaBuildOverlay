# 자동 업데이트

## 사용 방법

v0.1.11 전체 패키지를 한 번 설치하면 자동 업데이트가 켜집니다. 모드가 정상 plugins 경로에 있을 때 게임 시작마다 GitHub의 최신 정식 릴리즈를 한 번 확인하고, 새 버전을 백그라운드로 다운로드합니다. `새 버전 … · 다음 실행 때 적용됩니다` 버블이 뜨면 평소처럼 게임을 종료하세요. 다음 실행 전에 업데이트를 적용하고 완료 버블을 표시합니다. 자동 업데이트가 게임을 플레이하거나 재시작하지는 않으며, 게임 자원도 소비하지 않습니다.

`BepInEx/config/io.github.sephiria.build-overlay.cfg`의 `[Updates] Enabled = false`로 다운로드와 준비된 업데이트 적용을 끕니다. 끄기 전에 DLL 교체가 중단됐다면 복구는 수행합니다. ScriptEngine이나 다른 경로의 개발 DLL은 자동 업데이트하지 않습니다.

## 검증과 범위

- 고정 저장소 `westernbear/SephiriaBuildOverlay`의 latest API만 사용합니다. 인증·쿠키는 보내지 않으며 draft/prerelease/같은 버전/이전 버전을 적용하지 않습니다. 버전은 숫자로 비교합니다. 공개 릴리즈는 인증 없이 조회할 수 있습니다. [GitHub API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release)
- HTTPS의 고정 GitHub 릴리즈 경로와 공식 자산 리다이렉트 호스트만 허용합니다. 전체 확인/다운로드는 45초, JSON 256 KiB, 체크섬 1 KiB, ZIP 8 MiB 제한입니다. 응답 헤더가 없어도 스트리밍 바이트 수를 검사합니다.
- GitHub 자산의 SHA-256 digest, 배포 `.sha256`, 실제 ZIP 해시/길이가 일치해야 합니다. 전송 중 손상이나 잘못된 배포 파일을 확인하는 검사입니다. 별도 서명은 없으므로 저장소 계정이 탈취된 경우까지 보호하지는 못합니다. [자산 digest 필드](https://docs.github.com/en/rest/releases/releases#list-releases)
- ZIP은 128항목/총 해제 크기 32 MiB 제한이며 경로 이탈·중복·링크를 거부합니다. 배포본에 `update-protocol.txt`의 프로토콜 1/동일 버전이 있어야 합니다. DLL은 각각 4 MiB 이하, 정확한 어셈블리 이름·버전이어야 합니다.
- Core.dll과 Plugin.dll만 `BepInEx/cache/SephiriaBuildOverlayUpdater/pending`에 보관합니다. ZIP 안의 로더, 패처, 설정과 다른 파일은 추출하지 않습니다. 게임 경로의 junction/symlink를 따라가지 않습니다.

## 적용과 복구

독립 `BepInEx/patchers/SephiriaBuildOverlay.Updater.dll`이 두 플러그인 어셈블리 로드 전에 적용합니다. Core/Unity/Newtonsoft를 참조하지 않고 BepInEx와 배포 로더에 맞는 Mono.Cecil 0.10.4만 참조합니다. `TargetDLLs`, `Patch`, `Initialize` 계약을 사용하며 게임 어셈블리를 패치하지 않습니다. [BepInEx preloader 문서](https://docs.bepinex.dev/articles/dev_guide/preloader_patchers.html)

설치 DLL의 해시가 다운로드 당시와 다르면 준비된 업데이트를 버립니다. 수동 설치나 더 최신 버전을 오래된 다운로드로 덮지 않도록 하는 검사입니다. 다른 게임 프로세스가 있거나 모드 어셈블리가 이미 로드됐다면 적용을 미룹니다.

두 DLL의 백업을 검사하고 교체 기록인 journal을 디스크에 저장한 뒤, 같은 볼륨에서 파일을 교체합니다. 두 번째 교체가 실패하면 둘 다 복구합니다. 복구 도중 종료되더라도 다음 시작 때 journal을 먼저 처리합니다. 교체 성공을 기록한 뒤 정리하다 중단됐다면 새 버전을 유지합니다.

백업이 손상됐거나 교체 과정 밖에서 파일을 수동 변경했다면 임의로 덮지 않습니다. journal을 복구할 수 없으면 오버레이를 비활성화합니다.

일반 네트워크 실패는 `BepInEx/LogOutput.log`에 남기며 현재 버전으로 플레이합니다. 적용/복구 상세 로그의 이름은 `BuildOverlayUpdater`입니다. 쓰기 권한을 얻기 위해 관리자 승격하거나 ACL을 변경하지 않습니다.

### 수동 복구

게임을 종료하고 최신 전체 패키지의 모드 DLL 두 개와 패처를 다시 설치하세요. 교체가 중단된 상태라면 먼저 `BepInEx/cache/SephiriaBuildOverlayUpdater`를 백업하고, 다른 이름으로 이동해 journal을 보존하세요. 설정·세이브·다른 모드는 제거하지 마세요.

### 향후 릴리즈

프로토콜 1은 기존 패처 및 런타임 의존성을 그대로 쓰는 두 DLL 업데이트용입니다. 패처 자체·BepInEx·추가 의존성 변경에는 수동 설치가 필요합니다. 호환성이 바뀌면 배포의 프로토콜 값을 올려 이전 업데이트 모듈이 거부하게 해야 합니다. 로드된 패처를 자체 교체하지 않습니다.

## 재현 가능한 검증

`dotnet test SephiriaBuildOverlay.sln -c Release`는 HTTP 모의 응답과 실제 DLL 메타데이터를 사용해 다운로드, 준비, 실패 주입, 롤백·다음 실행 복구를 검사합니다. 실제 공개 릴리즈 준비는 별도 테스트용 BepInEx 폴더에 이전 두 DLL을 복사한 후 실행할 수 있습니다.

```powershell
dotnet run --project tools/SephiriaBuildOverlay.UpdateProbe -c Release -- stage-latest '<테스트 BepInEx 폴더>'
```

이 명령은 게임을 조작하거나 설치 DLL을 교체하지 않습니다. 알려진 로컬 릴리즈 준비는 `stage-package '<BepInEx 폴더>' '<릴리즈.zip>' '0.1.11'`입니다. 로컬 패키지 명령은 네트워크/digest 검사를 대신하지 않으므로 신뢰할 수 있는 자체 빌드 테스트에만 사용하세요. 실제 게임의 다음 시작 적용은 [정상 종료 검사](FAST_TESTING.md)로 타이틀에서만 확인합니다.
