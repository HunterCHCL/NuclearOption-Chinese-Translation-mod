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
        # 1. 读取翻译后的文本 (按行读取)
        with open(txt_translated, 'r', encoding='utf-8') as f:
            translations = [line.strip().replace('\\n', '\n') for line in f if line.strip()]

        # 2. 读取原始 missing.json
        with open(missing_json_path, 'r', encoding='utf-8') as f:
            missing_data = json.load(f)

        # 3. 按照当时 export 的逻辑，找出所有需要回填的 Key
        # 顺序必须与 export 时完全一致
        keys_to_fill = []
        for key, value in missing_data.items():
            default_value = key
            if key.startswith("[") and "]" in key:
                parts = key.split("]", 1)
                if len(parts) > 1:
                    default_value = parts[1]
            
            if value == default_value:
                keys_to_fill.append(key)

        if len(keys_to_fill) != len(translations):
            print(f"错误: 待回填的条目数({len(keys_to_fill)})与翻译文件行数({len(translations)})不匹配！")
            return

        # 4. 按顺序回填翻译
        updated_count = 0
        for key, trans_val in zip(keys_to_fill, translations):
            missing_data[key] = trans_val
            updated_count += 1

        # 5. 写入新文件 missingCN.json
        with open(output_json_path, 'w', encoding='utf-8') as f:
            json.dump(missing_data, f, ensure_ascii=False, indent=2)
        
        print(f"成功生成 {output_json_path}")
        print(f"共回填 {updated_count} 条翻译。")

    except Exception as e:
        print(f"处理失败: {str(e)}")

if __name__ == "__main__":
    create_missing_cn()
