import json
import tempfile
import unittest
from pathlib import Path
from prepare_demo import prepare


class DemoPruningTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.source = Path(self.temp.name) / 'game'
        self.output = self.source.parent / '.local-build/demo-project'
        (self.source / 'Data/Json').mkdir(parents=True)
        self.rows = {
            'tbitem': [dict(Id=1, BuildChannelMask=4, AcquisitionType=1, ItemType=1,
                           SkinId=1, SafeResourceId=0, IconPath='v3/ItemIcon/dog.png', AssetPathList=[])],
            'tbdogskin': [dict(Id=1, BuildChannelMask=4, FolderPath='v3/Shiba/Red', FixedEyewear='v2/Eyewear/a.png'),
                          dict(Id=2, BuildChannelMask=8, FolderPath='v3/Shiba/Red', FixedEyewear='')],
            'tbblindbox': [], 'tbblindboxschedule': [], 'tblinktree': [],
            'tbequipmentslotconfig': [], 'tbblindboxitemweight': [],
            'tbblindboxrarityrate': [], 'tbrefreshmentconfig': []}
        for rel in ('v3/Shiba/Red/nose.png', 'v3/Shiba/Red/ear.png',
                    'v3/Corgi/Yellow/head.png', 'v3/Shiba/RedOther/head.png',
                    'v3/ItemIcon/dog.png', 'v2/Eyewear/a.png', 'v2/Eyewear/b.png'):
            file = self.source / 'Assets' / rel
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_bytes(b'test')
        (self.source / 'project.godot').write_text('')
        (self.source / 'LuckyDogRise.csproj').write_text('<SteamworksNetRoot>old</SteamworksNetRoot>')

    def run_prepare(self):
        for name, rows in self.rows.items():
            (self.source / 'Data/Json' / (name + '.json')).write_text(json.dumps(rows))
        return prepare(self.source, self.output)

    def test_shared_folder_and_future_breed(self):
        result = self.run_prepare()
        self.assertTrue((self.output / 'Assets/v3/Shiba/Red/ear.png').exists())
        self.assertFalse((self.output / 'Assets/v3/Corgi/Yellow/head.png').exists())
        self.assertFalse((self.output / 'Assets/v3/Shiba/RedOther/head.png').exists())
        self.assertTrue((self.output / 'Assets/v2/Eyewear/a.png').exists())
        self.assertFalse((self.output / 'Assets/v2/Eyewear/b.png').exists())
        self.assertEqual(result['dog_skin_ids'], [1])
        self.assertTrue((self.source / 'Assets/v3/Corgi/Yellow/head.png').exists())

    def test_missing_safe_resource_fails(self):
        self.rows['tbitem'][0]['SafeResourceId'] = 99
        with self.assertRaisesRegex(ValueError, 'SafeResourceId'):
            self.run_prepare()

    def test_missing_reward_fails(self):
        self.rows['tblinktree'] = [dict(BuildChannelMask=4, RewardItemId=99, RewardBlindBoxId=0)]
        with self.assertRaisesRegex(ValueError, 'LinkTree reward'):
            self.run_prepare()

    def test_no_stale_import_cache(self):
        self.run_prepare()
        cache = self.output / '.godot/imported/old.ctex'
        cache.parent.mkdir(parents=True)
        cache.write_bytes(b'old')
        self.run_prepare()
        self.assertFalse(cache.exists())

    def test_wrong_output_rejected(self):
        with self.assertRaisesRegex(ValueError, 'Output must be'):
            prepare(self.source, self.source)

    def test_room_lab_is_not_copied_to_demo(self):
        # Room development must not leak scenes, executable C# or placeholder art.
        paths = ('Scenes/Dev/Rooms/RoomLab.tscn',
                 'Scenes/Dev/Rooms/InGameRoomPage.tscn',
                 'Scenes/Dev/Rooms/InGameRoomText.csv',
                 'Scenes/Dev/Rooms/RoomDesktopPreview.tscn',
                 'Scripts/Dev/Rooms/RoomDesktopPreview.cs',
                 'Scripts/Dev/Rooms/ModeManager.Rooms.cs',
                 'Scripts/Dev/Rooms/InGameRoomPreview.cs',
                 'Scripts/Dev/Rooms/InGameRoomSmoke.cs',
                 'Scripts/Dev/Rooms/RoomSandbox.cs',
                 'Assets/UI/Icon/Dev/Icon_RoomChat.svg')
        for rel in paths:
            file = self.source / rel
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_text('room-lab-placeholder')
        self.run_prepare()
        for rel in paths:
            self.assertFalse((self.output / rel).exists(), rel)
            self.assertTrue((self.source / rel).exists(), rel)

    def test_shared_room_code_is_not_copied_to_demo(self):
        excluded = ('Scripts/Rooms/RoomSession.cs',
                    'Scripts/Rooms/RoomSession.cs.uid',
                    'Scripts/Rooms/Transport/RoomTransport.cs')
        retained = ('Scripts/Desktop/SettingsManager.cs',
                    'Scripts/RoomsHelper.cs',
                    'Scripts/Other/Rooms/Unrelated.cs')
        contents = {rel: 'source-content:' + rel for rel in excluded + retained}
        for rel, content in contents.items():
            file = self.source / rel
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_text(content)
        self.run_prepare()
        self.assertFalse((self.output / 'Scripts/Rooms').exists())
        for rel in excluded:
            self.assertFalse((self.output / rel).exists(), rel)
        for rel in retained:
            self.assertEqual((self.output / rel).read_text(), contents[rel], rel)
        for rel, content in contents.items():
            self.assertEqual((self.source / rel).read_text(), content, rel)


if __name__ == '__main__':
    unittest.main()
