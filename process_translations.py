import json
import os
import re

# ================= 配置部分 =================

# 1. 并列模式：检测到 Key 时，在原有的 Value 后面加上译名 (格式: 原文 -> 原文 (译名))
# 适用于：原本是英文，你想保留英文并在后面加中文括注的情况
APPEND_DICT = {
    "Revoker": "制裁者",
    "Brawler": "斗士",
    "Chicane": "影袭者",
    "Ibis": "朱鹭",
    "Ternion": "多面手",
    "Ifrit": "妖灵",
    "Vortex": "涡流",
    "Medusa": "美杜莎",
    "Tarantula": "狼蛛",
    "Hydra":"九头蛇",
    "Compass":"罗盘",
    "Cricket": "蟋蟀",
    "Darkreach":"暗界",
    "Stratolance": "平流层之矛",
    "Spearhead": "先锋",
    "AeroSentry":"天空卫士",
    "Anvil": "铁砧",
    "Boltstrike": "雷霆打击者",
    "Linebreaker": "冲击者",
    "Jackknife":"折叠刀",
    "Annex": "侵占者",
    "Hyperion": "东方之柱",
    "Dynamo":"活力",
    "Shard": "碎片",
    "Piledriver": "打桩机",
    "Auger": "螺旋钻",
    "Scythe": "镰刀",
    "Tusko-B":"蛮犀-B",
    "Tusko":"蛮犀",
    "Lynchpin":"中枢",
    "Kingpin":"头目",
}

# 2. 替换模式：检测到 Key 时，直接把 Value 换成目标译名
# 适用于：统一武器、装备、地名的专业术语
REPLACE_DICT = {
    "毫米": "mm",
    "千克": "kg",
    "公斤": "kg",
    "千米": "km",
    "千瓦": "kw",
    "千吨": "kt",
    "米每秒": "m/s",
    "米": "m",
    "秒": "s",
    "千米每小时": "km/h",
    "公里每小时": "km/h",
    "公里/小时": "km/h",
    "千米/小时": "km/h",
    "公里": "km",
}

# 翻译文件的路径
FILE_PATH = r"e:\SteamLibrary\steamapps\common\Nuclear Option\BepInEx\plugins\NuclearOptionChinese\translation.json"

# 是否使用括号模式
# 0: 不带括号，中间隔空格 (例如: Revoker 制裁者)
# 1: 带括号，不隔空格     (例如: Revoker(制裁者))
USE_BRACKETS = 1

# ================= 脚本逻辑 =================

def process_translations():
    if not os.path.exists(FILE_PATH):
        print(f"错误: 找不到文件 {FILE_PATH}")
        return

    try:
        # 读取文件内容
        with open(FILE_PATH, 'r', encoding='utf-8') as f:
            content = f.read()
        
        # [优化]：去除 JSON 中的 C-style 注释 (// ...)，因为 Python 原生 json 库不支持它
        content = re.sub(r'^\s*//.*$', '', content, flags=re.MULTILINE)
        content = re.sub(r'\s//(?!"|.*").*$', '', content, flags=re.MULTILINE)
        
        # 处理可能出现的末尾逗号 (当某些项被注释掉后容易产生)
        content = re.sub(r',\s*([\]}])', r'\1', content)

        # 解析 JSON
        data = json.loads(content)

        count_append = 0
        count_replace = 0

        # 遍历字典
        # 脚本只更改 Value 部分，Key (左侧原文) 保持不动
        for key in data.keys():
            original_val = data[key]
            
            # --- 1. 处理并列字典 (APPEND_DICT) ---
            for item_name, chinese_name in APPEND_DICT.items():
                if item_name in original_val:
                    # [新增逻辑]：如果原文后面紧跟着特殊符号（如 -），则跳过此项，不进行并列标记
                    # 例如 "Revoker-1" 这种带有连字符的编号不应该被标记为 "Revoker(制裁者)-1"
                    if re.search(re.escape(item_name) + r'[-_#]', original_val):
                        continue

                    # 目标样式
                    if USE_BRACKETS == 1:
                        target_style = f"{item_name}({chinese_name})"
                    else:
                        target_style = f"{item_name} {chinese_name}"

                    # 定义我们要“纠正”或“补充”的几种情况：
                    # A. 已经有括号的 (无论里面写了啥) -> Revoker(xxx)
                    # B. 没括号但后面跟着译名的 (无论有没有空格) -> Revoker 制裁者 / Revoker制裁者
                    # C. 孤立的原文 -> Revoker
                    
                    # 使用 re.sub 的回调函数进行全局精准处理
                    pattern = re.escape(item_name) + r'(\s*\([^)]*\))|(' + re.escape(item_name) + r'\s*' + re.escape(chinese_name) + r')|(' + re.escape(item_name) + r')'
                    
                    def append_callback(m):
                        # 如果已经匹配到了 target_style，保持不变
                        if m.group(0) == target_style:
                            return m.group(0)
                        # 否则，所有匹配到的形式（旧译名、错位空格、孤立原文）全都统一为 target_style
                        return target_style

                    new_val = re.sub(pattern, append_callback, original_val)
                    if new_val != original_val:
                        data[key] = new_val
                        original_val = new_val
                        count_append += 1

            # --- 2. 处理替换字典 (REPLACE_DICT) ---
            # 按长度从长到短排序，防止先替换了短的（比如“米”），导致长的（比如“毫米”）变成“毫m”
            sorted_replace_keys = sorted(REPLACE_DICT.keys(), key=len, reverse=True)

            for term in sorted_replace_keys:
                target = REPLACE_DICT[term]
                
                # 判断是否包含中文
                has_chinese = any('\u4e00' <= c <= '\u9fa5' for c in term)
                
                if has_chinese:
                    # 对于中文术语/单位：
                    # 1. (?<![\u4e00-\u9fa5])：前面不能是中文（防止“厘米”匹配到“米”）
                    # 2. 我们不加后边界限制，以支持“米以上”、“米/秒”等组合词中的单位替换
                    pattern = r'(?<![\u4e00-\u9fa5])' + re.escape(term)
                else:
                    # 对于纯英文术语：使用 \b 单词边界保护
                    pattern = r'\b' + re.escape(term) + r'\b'
                
                # 检查是否真的有变化
                new_val = re.sub(pattern, target, original_val)
                if new_val != original_val:
                    data[key] = new_val
                    original_val = new_val
                    count_replace += 1

        # 写回文件
        with open(FILE_PATH, 'w', encoding='utf-8') as f:
            json.dump(data, f, ensure_ascii=False, indent=2)

        print(f"处理完成！")
        print(f"并列更新: {count_append} 处")
        print(f"替换更新: {count_replace} 处")

    except Exception as e:
        print(f"运行出错: {e}")

if __name__ == "__main__":
    process_translations()
