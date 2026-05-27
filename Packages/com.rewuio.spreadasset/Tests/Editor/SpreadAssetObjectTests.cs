using System;
using System.Reflection;
using SpreadAsset.Editor;
using NUnit.Framework;
using UnityEngine;

namespace SpreadAsset.Tests.Editor
{
    public sealed class SpreadAssetObjectTests
    {
        [Test]
        public void SpreadAssetObject_IsScriptableObject()
        {
            Assert.That(typeof(ScriptableObject).IsAssignableFrom(typeof(TestAsset)), Is.True);
        }

        private sealed class TestAsset : SpreadAssetObject
        {
        }
    }

    public sealed class SpreadAssetCodeGeneratorTests
    {
        [Test]
        public void GenerateRuntimeCode_AllowsCustomUnitySerializableDataFields()
        {
            SpreadAssetGenerationRequest request = CreateCustomTypeRequest();

            InvokeSpreadAssetCodeGenerator("ValidateRequest", request);
            string runtimeCode = (string)InvokeSpreadAssetCodeGenerator("GenerateRuntimeCode", request);

            StringAssert.Contains("using System.Collections.Generic;", runtimeCode);
            StringAssert.Contains("[SerializeField] private AnimationCurve _easeCurve;", runtimeCode);
            StringAssert.Contains("public AnimationCurve EaseCurve => _easeCurve;", runtimeCode);
            StringAssert.Contains("[SerializeField] private Gradient _tintGradient;", runtimeCode);
            StringAssert.Contains("[SerializeField] private List<float> _weights;", runtimeCode);
        }

        [Test]
        public void RecommendedDataFieldTypes_DoNotHardcodeAnimationCurve()
        {
            Type utilityType = typeof(SpreadAssetCodeGenerator).Assembly.GetType(
                "SpreadAsset.Editor.SpreadAssetFieldTypeUtility");
            FieldInfo field = utilityType.GetField(
                "RecommendedDataFieldTypeNames",
                BindingFlags.Public | BindingFlags.Static);

            string[] typeNames = (string[])field.GetValue(null);

            CollectionAssert.DoesNotContain(typeNames, "AnimationCurve");
        }

        private static SpreadAssetGenerationRequest CreateCustomTypeRequest()
        {
            return new SpreadAssetGenerationRequest
            {
                AssetClassName = "CurveDataAsset",
                NamespaceName = "SpreadAsset.Generated.Tests",
                MenuPath = "SpreadAsset/curve_data",
                Schema = new SpreadAssetDocumentSchema
                {
                    Tables = new[]
                    {
                        new SpreadAssetSchemaTable
                        {
                            RowTypeName = "CurveData",
                            FieldName = "CurveDatas",
                            Fields = new[]
                            {
                                new SpreadAssetSchemaField
                                {
                                    TypeName = "int",
                                    Name = "Id",
                                    IsKeyField = true
                                },
                                new SpreadAssetSchemaField
                                {
                                    TypeName = "AnimationCurve",
                                    Name = "EaseCurve"
                                },
                                new SpreadAssetSchemaField
                                {
                                    TypeName = "Gradient",
                                    Name = "TintGradient"
                                },
                                new SpreadAssetSchemaField
                                {
                                    TypeName = "List<float>",
                                    Name = "Weights"
                                }
                            }
                        }
                    }
                }
            };
        }

        private static object InvokeSpreadAssetCodeGenerator(string methodName, SpreadAssetGenerationRequest request)
        {
            MethodInfo method = typeof(SpreadAssetCodeGenerator).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, new object[] { request });
        }
    }
}
