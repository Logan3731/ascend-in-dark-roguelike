# VARCO 세계관 이미지 기록

장면: 1층의 거대한 문과 그 옆의 곡괭이·도끼
도구: VARCO 플로우 (TextInput → GenerateImage, 16:9, 모델 gpt-image-2.5-flare)
레퍼런스: 아이소메트릭 시점의 어두운 돌 던전 로그라이크 스크린샷 3장 (스타일 참고용, 업로드하지 않고 프롬프트로 묘사)

## 프롬프트 비교 설계
세 장은 "무엇을 바꾸면 이미지가 어떻게 달라지는지" 비교하려고 한 번에 한 축씩 바꿨다.

| # | 바꾼 점 | 의도 |
|---|---|---|
| A | 기준안: 아이소메트릭, 차갑고 어두운 조명 | 레퍼런스 게임 화면과 가장 가까운 결과 |
| B | A + 이끼·습기, 도구 근처의 따뜻한 빛, 문이 살짝 열림 | 분위기 키워드(축축함·이끼·따뜻한 불빛·쓸쓸함) 반영 |
| C | 시점을 로우앵글 시네마틱 키아트로 변경 | 거대함·웅장함 강조, 게임 화면이 아닌 대표 일러스트 |

### A
```
Isometric top-down view of a dark roguelike dungeon game scene. The ground floor of a windowless ancient stone tower: dark grey stone brick walls and worn stone floor tiles. In the center of the back wall stands a massive, towering ancient stone door, closed, carved with faint worn runes. Leaning against the wall right beside the door are an old iron pickaxe and a wooden-handled axe. Very dim, cold lighting, most of the room fades into pure black darkness at the edges. Painterly realistic 2D game art style, muted desaturated colors, quiet and lonely atmosphere. No characters, no UI, no text.
```

### B
```
Isometric top-down view of a dark roguelike dungeon game scene. The ground floor of a windowless ancient stone tower: damp dark stone brick walls with patches of green moss, cracked stone floor tiles. A massive, towering ancient stone door dominates the back wall, carved with faint worn runes, slightly ajar with a thin line of darkness behind it. Leaning against the wall beside the door are an old iron pickaxe and a wooden-handled axe. A small warm orange light glows near the tools, creating a soft circle of light, while everything outside the circle sinks into deep black darkness. Strong contrast between warm firelight and cold stone. Painterly realistic 2D game art style, quiet, lonely, mysterious atmosphere. No characters, no UI, no text.
```

### C
```
Cinematic low-angle key art. Inside the ground floor of a colossal windowless stone tower, a gigantic ancient stone door rises into the darkness above, so tall its top disappears into black shadow, carved with worn ancient runes. At the foot of the door, small in comparison, an old iron pickaxe and a wooden-handled axe lean against the stone wall. A faint warm glow lights only the bottom of the door and the tools; the vast space around is dark, damp and mossy. Grand, ancient, solemn and lonely mood. Dark fantasy, painterly realistic illustration, muted colors with a single warm accent. No characters, no text.
```

## 결과 비교
- 결과 URL(VARCO): A `/api/objects/32bee5be42225545d673f1e354d0b988.png` / B `/api/objects/22524f5931751384d3f87c247717fe6f.png` / C `/api/objects/64c4ef4970e38316f0e530201861da13.png`
- 기획자 판단: A가 가장 마음에 듦. (A/B/C 비교 메모는 기획자가 보충)

## ✅ 최종 대표 키아트: `images/edit7.png` (7회차, 기획자 확정)

## 선택한 이미지와 이유 (1줄)
- 선택: **A** — 이유: 실제 게임 맵처럼 보이는 쿼터뷰 배경으로 게임의 분위기를 미리 짐작할 수 있고, 여기에 오른쪽에 주인공을 크게 넣으면 캐릭터의 퀄리티까지 한 장에 보여줄 수 있다고 판단했다.

## 수정 1회차 — A 편집 (EditImage)
- 요청: A를 더 밝게 + 메인 남자 캐릭터가 오른손에 검, 왼손에 횃불을 든 모습 추가
- 참고: 레퍼런스 게임 *Darkness Survival* 키아트(횃불을 치켜든 주인공)
- 편집 지시문:
```
Keep the same isometric stone room, the massive ancient stone door, and the pickaxe and axe leaning beside the door, with the same painterly realistic style. Make the whole scene noticeably brighter so the stone walls, floor tiles and carved runes on the door are clearly visible, while the far edges still fade into darkness. Add the main character standing in the room near the door, facing slightly toward the viewer: an adult human man in a ragged, torn, worn cloth tunic, short messy dark hair, lean but strong build. In his right hand he holds a simple iron sword pointed down; in his raised left hand he holds a burning wooden torch with an orange flame. The torch casts a warm orange light around him on the stone floor and walls. Quiet, lonely, mysterious mood. No UI, no text.
```
- 결과 URL(VARCO): `/api/objects/e53db1750c45e8f2a7ace1746c8967fb.png`
- 결과 비교: (기획자 확인 후 작성 — 밝기 변화, 캐릭터 추가 후 문·도구가 유지됐는지, 손 방향(오른손 검/왼손 횃불)이 맞는지)
- 기획자 판단: 대표 키아트로 사용. 다만 캐릭터가 맵 크기에 맞춰 작게 나와서, 레퍼런스 키아트처럼 오른쪽에 크게 배치하도록 2회차 수정.

## 수정 2회차 — 키아트 구도 (EditImage, 1회차 결과를 원본으로)
- 요청: 맵과 캐릭터 크기를 맞추지 말고, 캐릭터를 오른쪽 앞에 크게
- 편집 지시문:
```
Recompose this image as a game key art poster. Keep the isometric stone room with the massive ancient stone door and the pickaxe and axe beside it as the background, filling the left and center of the frame, with the same brightness and painterly realistic style. Move the main character out of the room scale: show the same man much larger in the right foreground, from the thighs up, about two thirds of the image height, not matching the map scale, like a character standing in front of the scene on a poster. He is an adult human man in a ragged, torn, worn cloth tunic with short messy dark hair. His left hand is raised high holding a burning wooden torch with an orange flame near the top right of the frame; his right hand holds a simple iron sword pointed down. The torchlight casts warm orange light on his face and body, the background stays cooler and darker. Leave some empty dark space in the upper left. No UI, no text.
```
- 의도: 왼쪽 위 빈 공간은 GDD에서 게임 제목을 얹을 자리
- 결과 URL(VARCO): `/api/objects/d442d53709172ab70f9e4ca8c1d3fb33.png`
- 기획자 판단: 구도는 좋음. 그런데 주인공이 너무 고퀄 → 레퍼런스(Darkness Survival 키아트)처럼 소박한 모바일 게임 CG 느낌으로 3회차 수정.

## 수정 3회차 — 주인공 렌더링 톤 낮추기 (EditImage, 2회차 결과를 원본으로)
- 요청: 주인공을 레퍼런스처럼 좀 더 '저퀄'(소박한 인디 모바일 게임 3D 캐릭터 느낌)로
- 편집 지시문:
```
Keep the exact same composition, pose, background, torch and sword. Only change how the man in the right foreground is rendered: make him look like a simple, low-budget indie mobile game 3D character render from the early 2010s. Smooth, slightly plastic CG shading, simple flat textures on the ragged cloth tunic, simplified hair as one smooth mass, a plain and slightly stiff face with little detail, soft and even lighting on him instead of dramatic realistic lighting, no fine skin pores, no detailed fabric wrinkles, no cinematic realism. He should look like a modest game character model pasted in front of the background, not a high-end illustration. Keep the background as it is. No UI, no text.
```
- 결과 URL(VARCO): `/api/objects/c9c950bbecb8ffa6753d592c9e21fd24.png`
- 기획자 판단: 이번엔 너무 저퀄. 레퍼런스처럼 조금만 높이고, 배경도 캐릭터 그림체에 맞추기로 → 4회차.
- 배운 점: '저퀄'을 부정적 단어(no detail, stiff, plastic)로 지시하니 과하게 내려감. 원하는 수준을 긍정적으로(어느 시대·어떤 품질의 CG인지) 구체화하는 편이 낫다.

## 수정 4회차 — 품질 중간값 + 배경 그림체 통일 (EditImage, 3회차 결과를 원본으로)
- 편집 지시문:
```
Keep the exact same composition, pose, torch, sword, stone door, pickaxe and axe. Raise the character quality a little: a clean semi-realistic 3D CG mobile game character, like a polished indie mobile game key art from the mid 2010s. Soft smooth shading, a natural but simple face with clear eyes, believable hair strands grouped in soft clumps, ragged cloth tunic with some simple folds and wear, warm torchlight on his face and arm. Not photorealistic and not a high-end painting, but clearly better than a stiff plastic model. Then render the background in the same semi-realistic 3D CG style as the character, with matching softness, texture detail, color grading and lighting, so the whole image looks like one unified game key art made by the same artist. Dark cool stone tones in the background, warm orange accent from the torch. No UI, no text.
```
- 결과 URL(VARCO): `/api/objects/c8426ad95f57b301dd2a5761d025a230.png`
- 결과 비교: 배경은 캐릭터와 그림체가 통일되어 만족. 캐릭터는 근육질·과장된 얼굴의 픽사풍 애니메이션 느낌이 되어 불만족.
- 배운 점: 'semi-realistic 3D CG', 'polished' 같은 말은 모델이 애니메이션 영화풍으로 해석할 수 있다. 말로만 설명하는 데 한계가 있어, 5회차에는 레퍼런스 이미지를 직접 첨부했다.

## 수정 5회차 — 레퍼런스 이미지 첨부 (EditImage + reference, 4회차 결과를 원본으로)
- 레퍼런스: Darkness Survival 키아트의 캐릭터 부분을 잘라 업로드(스타일·분위기 참고용, 인물 복제 금지 지시)
- 편집 지시문:
```
Keep the background, composition, pose, torch, sword, door, pickaxe and axe exactly as they are. Only redraw the man so his rendering style matches the reference image's character: a semi-realistic 3D CG game character like a 2010s dark survival mobile game key art. Realistic human proportions, lean and slightly thin body (not muscular, not heroic), a realistic tired and tense face with natural small eyes, muted desaturated skin tones, slightly grainy matte textures, dirty and worn pale grey-beige cloth tunic, hair as realistic messy short strands. Subdued, gritty and somber feel, lit mainly by the warm torch on one side with dark shadows on the other. Absolutely not cartoon, not Pixar or Disney animation style, no exaggerated features. Do not copy the reference person's identity, gender or clothing details; only match its rendering style and mood. No UI, no text.
```
- 결과 URL(VARCO): `/api/objects/91bfa3537a48387f640e18f58ccce739.png`
- 결과 비교: 애니메이션 느낌은 사라졌지만 사진처럼 사실적이고 디테일이 과함. 기획자: "너무 디테일해."

## 수정 6회차 — 레퍼런스 렌더링 방식 최대한 모방 (5회차 결과 + 레퍼런스)
- 기획자 요청: 레퍼런스 인물을 거의 그대로 베껴서 그림체를 맞춰달라
- 조정: 그림체·렌더링(부드러운 피부, 단순한 얼굴, 낮은 텍스처 해상도, 탁한 색)은 최대한 모방하되, 인물은 우리 남자 주인공으로 유지 (타 게임 캐릭터를 복제하면 제출용 GDD에 쓰기 곤란)
- 편집 지시문:
```
Keep the background, composition and pose exactly the same. Redraw the man so he looks as if he was rendered by exactly the same old game engine and artist as the character in the reference image. Copy the reference character's rendering as closely as possible: the same smooth, soft, slightly blurry 3D CG skin with no pores and no fine wrinkles, the same simple low-detail face with soft features, the same simple pale grey-beige sleeveless cloth tunic with a few soft folds and a simple belt, the same simple hair made of a few soft chunky strands, the same soft low-contrast lighting with a warm torch glow, the same low texture resolution and slightly washed-out muted colors, like an early 2010s mobile game character render. Much less detail and less sharpness than now, less realistic, not photographic. He remains an adult man with short messy brown hair, holding the raised torch in his left hand and the sword in his right hand. No UI, no text.
```
- 결과 URL(VARCO): `/api/objects/4229d3c67c98be8ce092417dfdbe5326.png`
- 결과 비교: 피부·옷·머리가 부드럽고 단순해져 레퍼런스의 2010년대 모바일 CG 톤에 가장 가까움. 배경도 함께 부드러워져 룬 디테일은 줄었지만 그림체는 통일됨.
- 파일: `images/` 폴더 (A.png, B.png, C.png, edit1~edit6.png)
- 기획자 판단: 배경은 6회차 것을 쓰고, 캐릭터는 4회차 캐릭터로.

## 수정 7회차 — 6회차 배경 + 4회차 캐릭터 합성 (6회차 결과를 원본, 4회차 결과를 레퍼런스로)
- 편집 지시문:
```
Keep this image's background exactly as it is: the same soft isometric stone room, stone door, pickaxe and axe, lighting and colors. Replace only the man in the right foreground with the man from the reference image, copied exactly: the same face, hairstyle, body, ragged brown tunic with rope belt, the same rendering style, shading and level of detail as in the reference image. Same position, size and pose: left hand raising the burning torch at the top right, right hand holding the sword pointed down. No UI, no text.
```
- 결과 URL(VARCO): `/api/objects/9f0d7ee51554e66c14907e832ed04915.png` (파일: `images/edit7.png`)
- 결과 비교: 6회차의 부드러운 배경(문·곡괭이·도끼 위치 동일)은 유지되고, 캐릭터는 4회차의 얼굴·체형·갈색 누더기 튜닉으로 교체됨. 원본+레퍼런스 조합으로 '배경/캐릭터 따로 고르기'가 가능했다.
