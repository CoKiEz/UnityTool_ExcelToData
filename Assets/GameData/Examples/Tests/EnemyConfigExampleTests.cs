using System;
using System.Linq;
using ExcelDataTool.Generated;
using ExcelDataTool.Runtime;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameData.Examples.Tests
{
    /// <summary>使用项目真实生成的 Enemy.json TextAsset 和 EnemyTable 验证运行时读取示例。</summary>
    /// <remarks>这些测试属于可选示例，通用 ExcelDataTool.Runtime 不引用它们或任何游戏配置类型。</remarks>
    public sealed class EnemyConfigExampleTests
    {
        private const string EnemyJsonPath = "Assets/GameData/Generated/Json/Enemy.json";

        /// <summary>实际导入的 TextAsset 可还原真实生成类型，全部属性与 JSON 中 ID 为 1001 的记录一致。</summary>
        [Test]
        public void GeneratedEnemyAsset_RoundTripsToStrongTypes()
        {
            TextAsset asset = ReadExampleAsset();
            EnemyTable table = JsonTableLoader.Load<EnemyTable>(asset);
            var jsonRows = (JArray)JObject.Parse(asset.text)["rows"];
            JObject expected = jsonRows.OfType<JObject>().Single(row => (int)row["id"] == 1001);
            EnemyRow enemy = table.rows.Single(row => row.id == 1001);
            Assert.That(table.rows.Count, Is.EqualTo(jsonRows.Count));
            Assert.That(enemy.name, Is.EqualTo((string)expected["name"]));
            Assert.That(enemy.maxHealth, Is.EqualTo((int)expected["maxHealth"]));
            Assert.That(enemy.attack, Is.EqualTo((int)expected["attack"]));
            Assert.That(enemy.moveSpeed, Is.EqualTo((float)expected["moveSpeed"]));
            Assert.That(enemy.isBoss, Is.EqualTo((bool)expected["isBoss"]));
        }

        /// <summary>组件直接引用真实 TextAsset 后可以查找示例敌人，并公开可访问的强类型结果。</summary>
        [Test]
        public void Component_LoadsReferencedAssetAndExposesEnemy()
        {
            var instance = new GameObject("EnemyConfigExample_Test");
            try
            {
                EnemyConfigExample example = instance.AddComponent<EnemyConfigExample>();
                example.SourceJson = ReadExampleAsset();
                EnemyRow enemy = example.LoadExample();
                Assert.That(enemy.id, Is.EqualTo(1001));
                Assert.That(example.LoadedEnemy, Is.SameAs(enemy));
                Assert.That(example.LoadedTable.rows, Does.Contain(enemy));
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        /// <summary>配置缺少示例 ID 时明确报错，并清空先前结果，避免把旧加载状态误认为本次成功。</summary>
        [Test]
        public void Component_MissingEnemyFailsWithoutStaleResult()
        {
            var instance = new GameObject("EnemyConfigExample_MissingEnemy_Test");
            var emptyAsset = new TextAsset("{\"rows\":[]}");
            try
            {
                EnemyConfigExample example = instance.AddComponent<EnemyConfigExample>();
                example.SourceJson = ReadExampleAsset();
                example.LoadExample();
                example.SourceJson = emptyAsset;
                Assert.Throws<InvalidOperationException>(() => example.LoadExample());
                Assert.That(example.LoadedTable, Is.Null);
                Assert.That(example.LoadedEnemy, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
                UnityEngine.Object.DestroyImmediate(emptyAsset);
            }
        }

        /// <summary>通过编辑器资产数据库获取真实 TextAsset；运行时实现本身不使用此 API。</summary>
        private static TextAsset ReadExampleAsset()
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(EnemyJsonPath);
            Assert.That(asset, Is.Not.Null, "请先导入示例 Enemy.json；此测试必须验证真实资产。");
            return asset;
        }
    }
}
