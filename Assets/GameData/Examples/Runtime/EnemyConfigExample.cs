using System;
using ExcelDataTool.Generated;
using ExcelDataTool.Runtime;
using UnityEngine;

namespace GameData.Examples
{
    /// <summary>演示从 TextAsset 读取 EnemyTable，并以强类型字段访问 ID 为 1001 的敌人静态配置。</summary>
    /// <remarks>把生成的 Enemy.json 拖入资产引用即可；本示例不负责玩家状态、存档或配置回写。</remarks>
    public sealed class EnemyConfigExample : MonoBehaviour
    {
        [SerializeField, Tooltip("引用工具生成的 Enemy.json；构建后通过 TextAsset 内容读取，无需 Assets 磁盘路径。")]
        private TextAsset enemyJson;

        /// <summary>获取或设置要演示读取的 JSON 文本资产。</summary>
        public TextAsset SourceJson
        {
            get => enemyJson;
            set => enemyJson = value;
        }

        /// <summary>获取最近一次成功加载的强类型敌人表；未加载或加载失败时为 null。</summary>
        public EnemyTable LoadedTable { get; private set; }

        /// <summary>获取最近一次成功找到的 ID 为 1001 的敌人配置；未加载或失败时为 null。</summary>
        public EnemyRow LoadedEnemy { get; private set; }

        /// <summary>读取配置并查找 ID 为 1001 的敌人，使示例结果可在游戏代码和测试中直接访问。</summary>
        /// <returns>从 JSON 反序列化得到的 EnemyRow；所有属性来自配置文件。</returns>
        /// <exception cref="ArgumentNullException">未设置有效的 JSON 文本资产。</exception>
        /// <exception cref="ArgumentException">资产文本为空。</exception>
        /// <exception cref="InvalidOperationException">配置中没有 ID 为 1001 的敌人。</exception>
        /// <exception cref="Newtonsoft.Json.JsonSerializationException">JSON 协议不符或字段不能转换为 EnemyTable。</exception>
        public EnemyRow LoadExample()
        {
            LoadedTable = null;
            LoadedEnemy = null;
            EnemyTable table = JsonTableLoader.Load<EnemyTable>(enemyJson);
            EnemyRow enemy = table.rows.Find(row => row.id == 1001);
            if (enemy == null)
                throw new InvalidOperationException("Enemy.json 中没有找到 id == 1001 的敌人配置，请检查示例表。");
            LoadedTable = table;
            LoadedEnemy = enemy;
            return enemy;
        }

        /// <summary>进入运行模式时加载示例，并把强类型字段输出到 Console，便于人工验收。</summary>
        private void Start()
        {
            try
            {
                EnemyRow enemy = LoadExample();
                Debug.Log("敌人配置读取成功：ID=" + enemy.id + "，名称=" + enemy.name +
                    "，最大生命值=" + enemy.maxHealth + "，攻击力=" + enemy.attack +
                    "，移动速度=" + enemy.moveSpeed + "，是否 Boss=" + enemy.isBoss, this);
            }
            catch (Exception exception)
            {
                Debug.LogError("敌人配置读取失败：" + exception.Message, this);
            }
        }
    }
}
