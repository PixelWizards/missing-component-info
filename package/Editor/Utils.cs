﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.UnityTestProtocol;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
#if UNITY_2021_3_OR_NEWER || UNITY_2022_1_OR_NEWER
using UnityEditor.SceneManagement; // PrefabStageUtility
#else
using UnityEditor.Experimental.SceneManagement; // PrefabStageUtility
#endif

// ReSharper disable CheckNamespace

namespace Needle.ComponentExtension
{
	internal class MemberInfo
	{
		public string Name;
		public string Value;
		public SerializedProperty Property;
	}

	internal class ScriptCandidate
	{
		public Type Type;
		public string FilePath;
		public Object Asset;
		public int Distance;
		public float Distance01;
	}

	public static class Utils
	{
		internal static void CollectMembersInfo(Object obj, string identifier, SerializedObject serializedObject, out List<MemberInfo> members)
		{
			members = null;
			if (string.IsNullOrEmpty(identifier)) return;
			string path = null;
			string[] lines = null;
			if (IsInPrefabState(out path))
			{
			}
			else
			{
				if (PrefabUtility.IsPartOfPrefabInstance(obj)) return;
				var scene = SceneManager.GetActiveScene();
				path = scene.path;
			}

			if (path != null)
				lines = File.ReadAllLines(path);

			if (lines?.Length > 0)
			{
				// TODO: this is the naive version, a component could be multiple times on an object and we also need to check for the gameobject id first and find the correct component etc
				var foundStart = false;
				foreach (var line in lines)
				{
					if (foundStart && line.StartsWith("---")) break;
					if (foundStart)
					{
						if (!line.Contains(":")) continue;
						members ??= new List<MemberInfo>();
						var member = new MemberInfo();
						members.Add(member);
						var values = line.Split(':');
						member.Name = values[0].Trim();
						member.Value = values[1].Trim();
						member.Property = serializedObject.FindProperty(member.Name);
					}
					else if (line.Contains(identifier))
					{
						foundStart = true;
					}
				}
			}
		}

		internal static bool CanShowProperties(Object obj)
		{
			var isInPrefab = IsInPrefabState(out _);
			return isInPrefab || !PrefabUtility.IsPartOfPrefabInstance(obj);
		}

		private static bool IsInPrefabState(out string path)
		{
			path = null;
			var stage = PrefabStageUtility.GetCurrentPrefabStage();
			if (stage) path = stage.assetPath;
			return stage;
		}

		internal static void TryFindCandidatesInAssembly(string typeInfo, out List<ScriptCandidate> candidates)
		{
			candidates = null;
			var parts = typeInfo.Split(',');
			if (parts.Length < 2) return;
			var assemblyName = parts[1].Trim();
			if (string.IsNullOrWhiteSpace(assemblyName)) return;
			var assembly = AppDomain.CurrentDomain.GetAssemblies();
			foreach (var asm in assembly)
			{
				var asmName = asm.GetName().Name;
				var checkAssembly = string.Equals(asmName, assemblyName, StringComparison.CurrentCultureIgnoreCase);
				if (!checkAssembly)
				{ 
					var assemblyNameDist = ComputeLevenshteinDistance(asmName, assemblyName);
					if (assemblyNameDist < 35) checkAssembly = true;
				}
				if (checkAssembly)
				{
					var scriptName = parts.FirstOrDefault()?.Trim();
					var types = asm.GetExportedTypes();
					foreach (var t in types)
					{
						var dist = ComputeLevenshteinDistance(scriptName, t.Name);
						const int maxDist = 15;
						// Debug.Log(t.Name + ": " + dist + " to " + scriptName);
						if (dist < maxDist)
						{
							var assets = AssetDatabase.FindAssets(t.Name);
							var expectedPath = "/" + t.Name + ".cs";
							foreach (var r in assets)
							{
								var path = AssetDatabase.GUIDToAssetPath(r);
								if (path.EndsWith(expectedPath))
								{
									candidates ??= new List<ScriptCandidate>();
									var cand = new ScriptCandidate();
									candidates.Add(cand);
									cand.Asset = AssetDatabase.LoadAssetAtPath<Object>(path);
									cand.FilePath = path;
									cand.Type = t;
									cand.Distance = dist;
									cand.Distance01 = dist / (float)maxDist;
									break;
								}
							}

							// hack test to try to find the original script path
							// it would possibly work if we serialize the guid to retrieve the artifact key
							// but it would require to have the Library still

							// var ti = AssemblyHelper.ExtractAssemblyTypeInfo(true);
							// var art = AssetDatabaseExperimental.LookupArtifact(new ArtifactKey(new GUID("7b4d9c98c950d5a4589c9131b00f01b3")));
							// if (art.isValid)
							// {
							// 	AssetDatabaseExperimental.GetArtifactPaths(art, out var paths);
							// 	foreach (var fp in paths)
							// 	{
							// 		var content = File.ReadAllBytes(Path.GetFullPath(fp));
							// 		var bytes = new List<byte>();
							// 		const int length = 500;
							// 		for (var i = 0; i < length; i++)
							// 		{
							// 			bytes.Add(content[(content.Length-1) - (length - i)]);
							// 		}
							// 		Debug.Log("-- " + Path.GetFullPath(fp));
							// 		Debug.Log(content.Length);
							// 		var str = Encoding.UTF8.GetString(content.ToArray());
							// 		Debug.Log(str);
							// 		var res = AssetDatabase.FindAssets("MyScriptWithProperties_");
							// 		foreach (var r in res)
							// 		{
							// 			Debug.Log(AssetDatabase.GUIDToAssetPath(r));
							// 		}
							// 		// using var reader = new StreamReader(fp);
							// 		// if (reader.BaseStream.Length > 100)
							// 		// {
							// 		// 	reader.BaseStream.Seek(-100, SeekOrigin.End);
							// 		// }
							// 		// string line;
							// 		// while ((line = reader.ReadLine()) != null)
							// 		// {
							// 		// 	if (string.IsNullOrWhiteSpace(line)) continue;
							// 		// 	Debug.Log(line);
							// 		// }
							// 	}
							// }
							// var guids = AssetDatabase.FindAssets("t:" + scriptName + ".cs");
							// foreach (var f in guids)
							// {
							// 	typeof(AssetDatabase).GetMethod("GetArtifactInfos")
							// 	
							// 	var path = AssetDatabase.GUIDToAssetPath(f);
							// 	Debug.Log(path);
							// }
						}
					}
				}

				if (candidates != null)
				{
					candidates.Sort((a, b) => a.Distance - b.Distance);
				}
			}
		}

		private static int ComputeLevenshteinDistance(string source1, string source2)
		{
			var source1Length = source1.Length;
			var source2Length = source2.Length;

			var matrix = new int[source1Length + 1, source2Length + 1];

			// First calculation, if one entry is empty return full length
			if (source1Length == 0)
				return source2Length;

			if (source2Length == 0)
				return source1Length;

			// Initialization of matrix with row size source1Length and columns size source2Length
			for (var i = 0; i <= source1Length; matrix[i, 0] = i++)
			{
			}
			for (var j = 0; j <= source2Length; matrix[0, j] = j++)
			{
			}

			// Calculate rows and collumns distances
			for (var i = 1; i <= source1Length; i++)
			{
				for (var j = 1; j <= source2Length; j++)
				{
					var cost = (source2[j - 1] == source1[i - 1]) ? 0 : 1;

					matrix[i, j] = Mathf.Min(
						Mathf.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
						matrix[i - 1, j - 1] + cost);
				}
			}
			// return result
			return matrix[source1Length, source2Length];
		}
	}

	internal struct GUIColorScope : IDisposable
	{
		private Color? prev;

		public GUIColorScope(Color col)
		{
			this.prev = GUI.color;
			GUI.color = col;
		}

		public void Dispose()
		{
			if (prev != null)
				GUI.color = prev.Value;
		}
	}
}