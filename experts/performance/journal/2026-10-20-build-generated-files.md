# build-generated-files: prefiltering URP в PC.asset, 2026-10-20

URP `ShaderBuildPreprocessor` перед каждой сборкой пишет `m_Prefilter*` во все URP-ассеты целевой платформы.
Уровень PC исключён для Android, Quest — для Standalone, поэтому Android-сборка трогает только Quest.asset,
Win64 — только PC.asset. В dev теперь значения свежей Win64-сборки (`m_PrefilteringModeAdditionalLight: 3`,
`m_PrefilterSoftShadowsQualityMedium: 1`, `m_PrefilterSoftShadows: 0`). На Quest и Play Mode закоммиченные
значения не влияют. Грабли: статические флаги URP (`s_KeepOffVariantForAdditionalLights`,
`s_UseSoftShadowQualityLevelKeywords`) не сбрасываются в процессе, и Win64-сборка после Quest в одном Editor
пишет в PC.asset Quest-значения 4/0/1 (лишние off-варианты шейдеров PC). Build-Game собирает каждую
платформу отдельным процессом, а изменения только `m_Prefilter*` в build-* checkout сам возвращает к индексу.
