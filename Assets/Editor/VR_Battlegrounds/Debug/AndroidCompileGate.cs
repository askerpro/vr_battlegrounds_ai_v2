using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEditor.Compilation;
using UnityEngine;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>
    /// Ворота компиляции под Android (уровень 0 из Docs/testing.md).
    ///
    /// Ловит класс ошибок, который не виден в редакторе: код, ссылающийся на
    /// editor-only сборки, компилируется в Editor и падает при сборке плеера.
    /// Так было с `using static Codice.Client.Commands.WkTree.WorkspaceTreeNode`
    /// в PlayerController (находка BUILD-01) и с Editor-скриптами внутри
    /// Assets/Scripts (коммит 3f74bb6).
    ///
    /// Полная сборка игры не нужна — компилируются только скрипты под целевую
    /// платформу, это занимает секунды вместо минут.
    ///
    /// Вызов:
    ///   — из редактора: Tools/VR Battlegrounds/Debug/Проверить сборку под Android
    ///   — из кода и агентом: AndroidCompileGate.Run()
    ///   — из CI: -executeMethod VrBattlegrounds.EditorTools.AndroidCompileGate.RunBatch
    /// </summary>
    public static class AndroidCompileGate
    {
        /// <summary>Маркер в консоли, по которому агент находит результат.</summary>
        public const string ResultMarker = "[AndroidCompileGate]";

        private const string OutputFolder = "Temp/AndroidCompileGate";

        /// <summary>Результат прогона ворот.</summary>
        public class Result
        {
            public bool Passed;
            public List<string> Errors = new List<string>();

            /// <summary>Однострочная сводка для консоли и для агента.</summary>
            public string Summary
            {
                get
                {
                    return Passed
                        ? ResultMarker + " PASS — скрипты компилируются под Android"
                        : ResultMarker + " FAIL — ошибок: " + Errors.Count;
                }
            }
        }

        [MenuItem("Tools/VR Battlegrounds/Debug/Проверить сборку под Android")]
        private static void RunFromMenu()
        {
            Result result = Run();

            if (result.Passed)
            {
                EditorUtility.DisplayDialog(
                    "Ворота сборки под Android",
                    "PASS\n\nСкрипты компилируются под Android.",
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "Ворота сборки под Android",
                    "FAIL — ошибок: " + result.Errors.Count + "\n\nПодробности в консоли.",
                    "OK");
            }
        }

        /// <summary>
        /// Компилирует скрипты проекта под Android и возвращает список ошибок.
        /// Ничего не меняет в проекте: активная платформа не переключается,
        /// результат пишется в Temp/ и не попадает в репозиторий.
        /// </summary>
        public static Result Run()
        {
            var result = new Result();
            var errors = new List<string>();

            // CompilePlayerScripts не возвращает ошибки напрямую — они приходят
            // событием на каждую скомпилированную сборку.
            Action<string, CompilerMessage[]> onAssemblyFinished = (assemblyPath, messages) =>
            {
                foreach (CompilerMessage m in messages)
                {
                    if (m.type != CompilerMessageType.Error)
                    {
                        continue;
                    }

                    string file = string.IsNullOrEmpty(m.file) ? "?" : m.file.Replace('\\', '/');
                    errors.Add(file + ":" + m.line + " — " + m.message);
                }
            };

            CompilationPipeline.assemblyCompilationFinished += onAssemblyFinished;

            try
            {
                Directory.CreateDirectory(OutputFolder);

                var settings = new ScriptCompilationSettings
                {
                    group = BuildTargetGroup.Android,
                    target = BuildTarget.Android,
                    options = ScriptCompilationOptions.None
                };

                PlayerBuildInterface.CompilePlayerScripts(settings, OutputFolder);
            }
            catch (Exception e)
            {
                errors.Add("Прогон ворот упал: " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                CompilationPipeline.assemblyCompilationFinished -= onAssemblyFinished;
            }

            // Одна ошибка может прийти несколько раз — по разу на сборку, которая её видит.
            result.Errors = errors.Distinct().ToList();
            result.Passed = result.Errors.Count == 0;

            if (result.Passed)
            {
                UnityEngine.Debug.Log(result.Summary);
            }
            else
            {
                UnityEngine.Debug.LogError(result.Summary + "\n" + string.Join("\n", result.Errors.ToArray()));
            }

            return result;
        }

        /// <summary>
        /// Точка входа для батч-режима: завершает Unity с кодом 1, если ворота красные.
        /// </summary>
        public static void RunBatch()
        {
            Result result = Run();
            EditorApplication.Exit(result.Passed ? 0 : 1);
        }
    }
}
