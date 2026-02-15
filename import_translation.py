import json
import os

def create_missing_cn():
    # 路径配置
    txt_original = 'to_translate.txt'      # 导出时的原文 TXT
    txt_translated = 'to_translateCN.txt'  # 用户翻译后的 TXT
    missing_json_path = r'e:\SteamLibrary\steamapps\common\Nuclear Option\BepInEx\plugins\NuclearOptionChinese\missing.json'
    output_json_path = r'e:\SteamLibrary\steamapps\common\Nuclear Option\BepInEx\plugins\NuclearOptionChinese\missingCN.json'

    if not os.path.exists(txt_original) or not os.path.exists(txt_translated):
        print(f"错误: 找不到 {txt_original} 或 {txt_translated}")
        return
    
    if not os.path.exists(missing_json_path):
        print(f"错误: 找不到源文件 {missing_json_path}")
        return

    try:
        # 1. 读取原文和翻译后的文本
        with open(txt_original, 'r', encoding='utf-8') as f:
            originals = [line.strip() for line in f if line.strip()]
        
        with open(txt_translated, 'r', encoding='utf-8') as f:
            translations = [line.strip() for line in f if line.strip()]

        if len(originals) != len(translations):
            print(f"警告: 行数不匹配！原文 {len(originals)} 行，翻译 {len(translations)} 行。")
            return

        # 创建映射表 (处理 \n -> 换行符)
        mapping = {}
        for orig, tran in zip(originals, translations):
            key = orig.replace('\\n', '\n')
            val = tran.replace('\\n', '\n')
            mapping[key] = val

        # 2. 读取原始 missing.json
        with open(missing_json_path, 'r', encoding='utf-8') as f:
            missing_data = json.load(f)

        # 3. 替换翻译
        new_data = {}
        updated_count = 0
        for key, value in missing_data.items():
            if key in mapping:
                new_data[key] = mapping[key]
                updated_count += 1
            else:
                new_data[key] = value

        # 4. 写入新文件 missingCN.json
        with open(output_json_path, 'w', encoding='utf-8') as f:
            json.dump(new_data, f, ensure_ascii=False, indent=2)
        
        print(f"成功生成 {output_json_path}")
        print(f"共处理 {len(missing_data)} 条文本，其中 {updated_count} 条已应用新翻译。")

    except Exception as e:
        print(f"处理失败: {str(e)}")

if __name__ == "__main__":
    create_missing_cn()
