import json
import os

def export_missing_to_txt():
    # 路径配置
    missing_file = r'e:\SteamLibrary\steamapps\common\Nuclear Option\BepInEx\plugins\NuclearOptionChinese\missing.json'
    output_file = 'to_translate.txt'

    if not os.path.exists(missing_file):
        print(f"错误: 找不到文件 {missing_file}")
        return

    try:
        with open(missing_file, 'r', encoding='utf-8') as f:
            data = json.load(f)
        
        # 提取待翻译文本
        # 我们按照 missing.json 的原始顺序提取所有未翻译的条目
        # 不进行去重，因为同一单词在不同作用域下可能需要不同的翻译
        export_items = []
        for key, value in data.items():
            default_value = key
            if key.startswith("[") and "]" in key:
                parts = key.split("]", 1)
                if len(parts) > 1:
                    default_value = parts[1]
            
            if value == default_value:
                # 记录该条目
                export_items.append(default_value)

        if not export_items:
            print("没有发现新的待翻译文本。")
            return

        with open(output_file, 'w', encoding='utf-8') as f:
            for text in export_items:
                # 替换换行符为 \n 字面量
                f.write(text.replace('\n', '\\n') + '\n')
        
        print(f"成功导出 {len(export_items)} 条文本到 {output_file}")
        print("注意: 文本中的换行符已转换为 \\n 以便批量处理。")

    except Exception as e:
        print(f"处理失败: {str(e)}")

if __name__ == "__main__":
    export_missing_to_txt()
