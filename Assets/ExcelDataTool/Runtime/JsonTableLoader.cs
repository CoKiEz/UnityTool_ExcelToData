using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ExcelDataTool.Runtime
{
    /// <summary>从已加载的文本资产或 JSON 文本读取强类型静态配置，不访问磁盘路径，也不依赖编辑器 API。</summary>
    public static class JsonTableLoader
    {
        /// <summary>把引用的 JSON 文本资产还原为生成的表容器，供构建后的 Player 直接读取。</summary>
        /// <typeparam name="TTable">包含 rows 集合的生成表类型，例如 EnemyTable。</typeparam>
        /// <param name="asset">由场景、Prefab 或项目资源加载方式提供的 JSON 文本资产。</param>
        /// <returns>包含强类型行数据的新表对象；每次调用均独立反序列化，不修改原始资产。</returns>
        /// <exception cref="ArgumentNullException">资产为空引用或已经被 Unity 销毁。</exception>
        /// <exception cref="ArgumentException">资产文本为空或仅包含空白。</exception>
        /// <exception cref="JsonSerializationException">JSON 损坏、缺少 rows 数组，或数据与目标表类型不匹配。</exception>
        public static TTable Load<TTable>(TextAsset asset) where TTable : class
        {
            if (asset == null)
                throw new ArgumentNullException(nameof(asset), "请先提供需要读取的 JSON TextAsset 资产。");
            return LoadJson<TTable>(asset.text);
        }

        /// <summary>把已有 JSON 文本还原为强类型表；只接受以 rows 数组为根成员的配置协议。</summary>
        /// <typeparam name="TTable">由导表工具生成、具有 rows 集合的表容器类型。</typeparam>
        /// <param name="json">已经读取到内存的 JSON 文本；本方法不读取文件、不回写配置。</param>
        /// <returns>包含强类型行数据的新表对象，允许 rows 为空数组。</returns>
        /// <exception cref="ArgumentNullException">JSON 文本为空引用。</exception>
        /// <exception cref="ArgumentException">JSON 文本为空或仅包含空白。</exception>
        /// <exception cref="JsonSerializationException">JSON 损坏、协议不符、包含未知成员或不能转换为指定表类型。</exception>
        public static TTable LoadJson<TTable>(string json) where TTable : class
        {
            if (json == null) throw new ArgumentNullException(nameof(json), "JSON 配置文本不能为 null。");
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("JSON 配置文本不能为空。", nameof(json));

            try
            {
                JObject document;
                using (var text = new StringReader(json))
                using (var reader = new JsonTextReader(text)
                {
                    Culture = CultureInfo.InvariantCulture,
                    DateParseHandling = DateParseHandling.None,
                    MaxDepth = 64
                })
                {
                    JToken root = JToken.Load(reader, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        LineInfoHandling = LineInfoHandling.Load
                    });
                    document = root as JObject;
                    if (document == null)
                        throw new JsonSerializationException("配置 JSON 根节点必须是包含 rows 数组的对象，不能使用裸数组或 null。");
                    if (reader.Read())
                        throw new JsonSerializationException("配置 JSON 根对象之后存在额外内容。");
                }

                if (!(document["rows"] is JArray rows))
                    throw new JsonSerializationException("配置 JSON 必须包含大小写准确的 rows 数组，不能缺失、为 null 或其他类型。");
                for (int index = 0; index < rows.Count; index++)
                {
                    if (!(rows[index] is JObject))
                        throw new JsonSerializationException("配置 JSON 的 rows[" + index + "] 必须是一条对象记录，不能为 null 或基础类型。");
                }

                // 使用 Create 而不是 CreateDefault，确保外部 JsonConvert.DefaultSettings 无法打开类型元数据处理。
                var serializer = JsonSerializer.Create(new JsonSerializerSettings
                {
                    Culture = CultureInfo.InvariantCulture,
                    TypeNameHandling = TypeNameHandling.None,
                    MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Error,
                    ObjectCreationHandling = ObjectCreationHandling.Replace,
                    DateParseHandling = DateParseHandling.None,
                    MaxDepth = 64
                });
                TTable table = document.ToObject<TTable>(serializer);
                if (table == null)
                    throw new JsonSerializationException("配置 JSON 未能创建目标表对象。");
                return table;
            }
            catch (JsonException exception)
            {
                throw new JsonSerializationException("无法读取配置表 " + typeof(TTable).FullName + "：" + exception.Message, exception);
            }
        }
    }
}
