using System;
using System.Collections.Generic;
using System.Globalization;
using ExcelDataTool.Runtime;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace ExcelDataTool.Tests.Runtime
{
    /// <summary>验证通用运行时读取器的 JSON 协议、强类型转换和类型元数据安全，不依赖游戏示例资产。</summary>
    public sealed class JsonTableLoaderTests
    {
        private const string ValidJson = "{\"rows\":[{\"id\":1001,\"name\":\"哥布林\",\"speed\":3.5,\"power\":1.25,\"isBoss\":false}]}";

        /// <summary>使用真实 Unity TextAsset 读取所有支持的基础字段类型，证明不需要磁盘路径。</summary>
        [Test]
        public void TextAsset_LoadsStronglyTypedFields()
        {
            var asset = new TextAsset(ValidJson);
            try
            {
                LoaderTable table = JsonTableLoader.Load<LoaderTable>(asset);
                Assert.That(table.rows.Count, Is.EqualTo(1));
                Assert.That(table.rows[0].id, Is.EqualTo(1001));
                Assert.That(table.rows[0].name, Is.EqualTo("哥布林"));
                Assert.That(table.rows[0].speed, Is.EqualTo(3.5f));
                Assert.That(table.rows[0].power, Is.EqualTo(1.25d));
                Assert.That(table.rows[0].isBoss, Is.False);
                Assert.That(asset.text, Is.EqualTo(ValidJson));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        /// <summary>空的 rows 数组仍是合法配置，并生成可直接访问的空集合。</summary>
        [Test]
        public void EmptyRows_ProducesEmptyCollection()
        {
            Assert.That(JsonTableLoader.LoadJson<LoaderTable>("{\"rows\":[]}").rows, Is.Empty);
        }

        /// <summary>同一份配置每次读取都产生独立对象，内存修改不会成为静态配置的隐式回写。</summary>
        [Test]
        public void RepeatedLoad_ProducesIndependentObjects()
        {
            LoaderTable first = JsonTableLoader.LoadJson<LoaderTable>(ValidJson);
            first.rows[0].name = "仅内存修改";
            LoaderTable second = JsonTableLoader.LoadJson<LoaderTable>(ValidJson);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.rows[0].name, Is.EqualTo("哥布林"));
        }

        /// <summary>读取数值不受当前系统区域的小数分隔符影响。</summary>
        [Test]
        public void NumericConversion_IsCultureIndependent()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                LoaderRow row = JsonTableLoader.LoadJson<LoaderTable>(ValidJson).rows[0];
                Assert.That(row.speed, Is.EqualTo(3.5f));
                Assert.That(row.power, Is.EqualTo(1.25d));
            }
            finally { CultureInfo.CurrentCulture = original; }
        }

        /// <summary>普通字符串即使看起来像日期也保持原文，不自动解释为日期对象。</summary>
        [Test]
        public void DateLikeText_RemainsString()
        {
            const string value = "2026-10-10T08:00:00+08:00";
            LoaderTable table = JsonTableLoader.LoadJson<LoaderTable>("{\"rows\":[{\"id\":1,\"name\":\"" + value + "\"}]}");
            Assert.That(table.rows[0].name, Is.EqualTo(value));
        }

        /// <summary>缺少 TextAsset 时返回明确的参数异常。</summary>
        [Test]
        public void NullAsset_IsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => JsonTableLoader.Load<LoaderTable>(null));
        }

        /// <summary>缺少 JSON 文本时返回明确的参数异常。</summary>
        [Test]
        public void NullJson_IsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => JsonTableLoader.LoadJson<LoaderTable>(null));
        }

        /// <summary>空文本不能被误当成无记录的合法表。</summary>
        /// <param name="json">空白测试文本。</param>
        [TestCase("")]
        [TestCase(" \r\n\t ")]
        public void EmptyJson_IsRejected(string json)
        {
            Assert.Throws<ArgumentException>(() => JsonTableLoader.LoadJson<LoaderTable>(json));
        }

        /// <summary>根节点、rows 类型和数组元素必须遵循固定导出协议。</summary>
        /// <param name="json">协议错误的测试 JSON。</param>
        [TestCase("[]")]
        [TestCase("null")]
        [TestCase("{}")]
        [TestCase("{\"Rows\":[]}")]
        [TestCase("{\"rows\":null}")]
        [TestCase("{\"rows\":{}}")]
        [TestCase("{\"rows\":[null]}")]
        [TestCase("{\"rows\":[1]}")]
        public void InvalidProtocol_IsRejected(string json)
        {
            JsonSerializationException error = Assert.Throws<JsonSerializationException>(() => JsonTableLoader.LoadJson<LoaderTable>(json));
            StringAssert.Contains("配置", error.Message);
        }

        /// <summary>截断内容、重复成员和额外根节点不得被静默接受。</summary>
        /// <param name="json">结构错误的测试 JSON。</param>
        [TestCase("{")]
        [TestCase("{\"rows\":[],\"rows\":[]}")]
        [TestCase("{\"rows\":[]} {\"rows\":[]}")]
        public void MalformedJson_IsRejected(string json)
        {
            Assert.Throws<JsonSerializationException>(() => JsonTableLoader.LoadJson<LoaderTable>(json));
        }

        /// <summary>数据不能转换或字段与生成类型不同步时应明确失败，避免悄悄丢失字段。</summary>
        /// <param name="json">包含类型错误或未知字段的测试 JSON。</param>
        [TestCase("{\"rows\":[{\"id\":\"无法转换\"}]}")]
        [TestCase("{\"rows\":[{\"id\":1,\"unknownField\":5}]}")]
        public void SchemaMismatch_IsRejected(string json)
        {
            Assert.Throws<JsonSerializationException>(() => JsonTableLoader.LoadJson<LoaderTable>(json));
        }

        /// <summary>即使外部全局设置允许类型元数据，读取器也不会实例化 JSON 指定的派生类型。</summary>
        [Test]
        public void TypeMetadata_CannotActivateTypesOrUseGlobalDefaults()
        {
            Func<JsonSerializerSettings> original = JsonConvert.DefaultSettings;
            MetadataProbeTable.ConstructionCount = 0;
            try
            {
                JsonConvert.DefaultSettings = () => new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };
                string json = "{\"$type\":\"" + typeof(MetadataProbeTable).AssemblyQualifiedName + "\",\"rows\":[]}";
                Assert.Throws<JsonSerializationException>(() => JsonTableLoader.LoadJson<LoaderTable>(json));
                Assert.That(MetadataProbeTable.ConstructionCount, Is.EqualTo(0));
            }
            finally { JsonConvert.DefaultSettings = original; }
        }

        /// <summary>测试用最小表容器，与生成的 rows 字段协议一致。</summary>
        [Serializable]
        public class LoaderTable
        {
            /// <summary>测试记录集合。</summary>
            public List<LoaderRow> rows = new List<LoaderRow>();
        }

        /// <summary>覆盖全部五种基础类型的测试数据行。</summary>
        [Serializable]
        public sealed class LoaderRow
        {
            /// <summary>整数标识。</summary>
            public int id;
            /// <summary>普通及中文文本。</summary>
            public string name = string.Empty;
            /// <summary>单精度数值。</summary>
            public float speed;
            /// <summary>双精度数值。</summary>
            public double power;
            /// <summary>布尔标记。</summary>
            public bool isBoss;
        }

        /// <summary>只用于检测类型元数据是否错误触发构造函数，不进行任何文件或系统操作。</summary>
        [Serializable]
        public sealed class MetadataProbeTable : LoaderTable
        {
            /// <summary>记录测试期间构造函数被调用的次数。</summary>
            public static int ConstructionCount { get; set; }

            /// <summary>在被构造时记录一次调用，供安全回归断言。</summary>
            public MetadataProbeTable()
            {
                ConstructionCount++;
            }
        }
    }
}
