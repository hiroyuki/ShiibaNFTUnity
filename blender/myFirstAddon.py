bl_info = {
    "name":"サンプルアドオン：単一のファイルで構成されるアドオン",
    "author":"ぬっぢ",
    "version": (1, 0, 0),
    "blender": (4,4, 0),
    "location":"",
    "description":"単一のファイルで構成されるアドオン",
    "warning":"",
    "support":"COMMUNITY"   ,
    "doc_url":"",
    "tracker_url":"",
    "category":"Sample"
}

def register ():
    print(f"アドオン「{bl_info['name']}」が有効化されました。")
def unregister():
    print(f"アドオン「{bl_info['name']}」が無効化されました。") 