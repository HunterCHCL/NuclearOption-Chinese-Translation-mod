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
        
        # 提取所有需要翻译的原文 (Key)
        # 排除掉已经翻译过的内容（如果 Value 和 Key 不一致的情况）
        lines_to_translate = [key for key, value in data.items() if key == value]

        if not lines_to_translate:
            print("没有发现新的待翻译文本。")
            return

        with open(output_file, 'w', encoding='utf-8') as f:
            for line in lines_to_translate:
                # 替换掉换行符，防止破坏 TXT 结构，翻译后再恢复即可
                # 或者直接原样输出，取决于用户的翻译工具
                f.write(line.replace('\n', '\\n') + '\n')
        
        print(f"成功导出 {len(lines_to_translate)} 条文本到 {output_file}")
        print("注意: 文本中的换行符已转换为 \\n 以便批量处理。")

    except Exception as e:
        print(f"处理失败: {str(e)}")

if __name__ == "__main__":
    export_missing_to_txt()
