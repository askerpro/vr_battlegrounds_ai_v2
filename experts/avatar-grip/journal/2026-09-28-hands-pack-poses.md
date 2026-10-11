# Позы хвата Gun_real сняты с клипа пака Hands Weapons Animations в универсальных осях ладони

Коммит `d48b106cf2e511372df0ea2a91433f767d3f96a7` (2026-09-28). Причина: позы SDK на трёхфаланговой
кисти MEF выглядели плохо. `HandsPackPoseExtractor` пишет повороты фаланг с кадра
`Hands_Gun@Aiming_Idle`; `HandsPackGripAligner` ставит трансформы выравнивания
`Grabs/HandsPack/<поза>/<аватар>/Left|Right` (масштаб пак → префаб ×0.707, вторая рука — зеркало);
`HandsPackPoseImporter` назначает позы записям MEF_Base_Avatar у Gun_real. Общие `Grabs/default` не тронуты.

Сторож `HandsPackHandPoseTests`: сгибы ±5° (большой палец ±20°), корпус в ладони ±5 мм/±3°, обе руки.
Результат прогона в сообщении коммита не указан — unknown. Копии поз в MEF_Rig/HandPoses/handspack
и правка Default — ручная доводка.

Источники: `git show d48b106c`; `Assets/Editor/VR_Battlegrounds/Avatars/HandsPackGripAligner.cs`.
