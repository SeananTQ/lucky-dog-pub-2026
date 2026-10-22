extends SceneTree
# Debug host-window regression. Run with --loot-validation-headless --dev-launcher.
# Uses an isolated user directory and startup Steam Mock; moves only the test window/pointer.
var failures = 0
var host
var viewport_container
var game
var bar
var welcome

func check(condition, message):
	if condition:
		print("[WelcomeIntegration] PASS ", message)
	else:
		failures += 1
		push_error("[WelcomeIntegration] FAIL " + message)

func move_pointer(point):
	var local = viewport_container.get_global_transform_with_canvas() * point
	Input.warp_mouse(local)
	var motion = InputEventMouseMotion.new()
	motion.position = local
	motion.global_position = local
	root.push_input(motion, true)
	await process_frame
	await process_frame

func mouse_button(point, button, pressed):
	var local = viewport_container.get_global_transform_with_canvas() * point
	var event = InputEventMouseButton.new()
	event.position = local
	event.global_position = local
	event.button_index = button
	event.pressed = pressed
	root.push_input(event, true)
	await process_frame
	await process_frame

func click_control(control):
	var point = control.get_global_rect().get_center()
	await move_pointer(point)
	await mouse_button(point, MOUSE_BUTTON_LEFT, true)
	await mouse_button(point, MOUSE_BUTTON_LEFT, false)

func click_host_control(control):
	var point = viewport_container.get_global_transform_with_canvas().affine_inverse() * control.get_global_rect().get_center()
	await move_pointer(point)
	await mouse_button(point, MOUSE_BUTTON_LEFT, true)
	await mouse_button(point, MOUSE_BUTTON_LEFT, false)

func advance_idle(seconds):
	game.call("UpdateQuickBarPointer", false, Vector2(1100, 950), false, false, seconds)

func check_debug_replay_settings():
	await click_control(welcome.get_node("InputShield/Center/Panel/Margin/Content/Confirm"))
	await move_pointer(Vector2(200, 40))
	await create_timer(.25).timeout
	await click_control(bar.get_node("Clip/Bar/Buttons/Help"))
	await click_control(game.get_node("PokerHandShowcase/Header/CloseButton"))
	var settings = host.get_node("SettingsPanel")
	settings.call("Open")
	await create_timer(.3).timeout
	await click_host_control(settings.get_node("Panel/RootVBox/TitleRow/DebugTab"))
	var debug_content = settings.get_node("Panel/RootVBox/Scroll/ContentVBox/DebugContent")
	var scroll = settings.get_node("Panel/RootVBox/Scroll")
	var welcome_toggle = debug_content.get_node("PokerWelcomeReplayRow/PokerWelcomeReplayToggle")
	var discovery_toggle = debug_content.get_node("PokerQuickBarReplayRow/PokerQuickBarReplayToggle")
	check(debug_content.is_visible_in_tree() and not welcome_toggle.button_pressed and not discovery_toggle.button_pressed,
		"Debug replay switches are visible and default to off")
	for toggle in [welcome_toggle, discovery_toggle]:
		scroll.ensure_control_visible(toggle)
		await process_frame
		await click_host_control(toggle)
		check(toggle.button_pressed, "Actual Debug switch click enables " + toggle.name)
		var saved = ConfigFile.new()
		saved.load("user://settings.cfg")
		check(saved.get_value("debug", "repeat_poker_welcome_on_startup", false), "Welcome replay is immediately saved")
		check(saved.get_value("debug", "repeat_poker_quick_bar_discovery_on_startup", false) == (toggle == discovery_toggle),
			"Discovery replay is independent and immediately saved")
	check(not welcome.get("IsOverlayVisible") and bar.get_node("HotZone").size.y == 80,
		"Changing replay switches preserves the current welcome and toolbar state")
	var reset = debug_content.get_node("ResetSettingsBtn")
	scroll.ensure_control_visible(reset)
	await process_frame
	await click_host_control(reset)
	var defaults = ConfigFile.new()
	defaults.load("user://settings.cfg")
	check(not welcome_toggle.button_pressed and not discovery_toggle.button_pressed
		and not defaults.get_value("debug", "repeat_poker_welcome_on_startup", false)
		and not defaults.get_value("debug", "repeat_poker_quick_bar_discovery_on_startup", false),
		"Reset settings clears both persisted preferences and refreshes both switches")
	settings.call("CloseImmediate")
	host.call("SwitchToBossKey")
	await process_frame
	host.call("SwitchToPlay")
	await create_timer(.3).timeout
	check(not welcome.get("IsOverlayVisible") and bar.get_node("HotZone").size.y == 80,
		"Replay preference changes and settings reset do not replay onboarding within the same run")

func check_idle_discovery(guide):
	await move_pointer(Vector2(1150, 600))
	bar.call("NotifyEffectiveInteraction")
	advance_idle(19)
	check(not bar.get("IsIdleReminderActive"), "No automatic reminder before 20 seconds")
	await mouse_button(Vector2(1150, 600), MOUSE_BUTTON_LEFT, true)
	await mouse_button(Vector2(1150, 600), MOUSE_BUTTON_LEFT, false)
	advance_idle(1.1)
	check(bar.get("IsIdleReminderActive") and bar.get("IsExpanded") and not guide.get("IsOverlayVisible"), "Idle/blank clicks reveal only the toolbar, even with the old setting disabled")
	advance_idle(60)
	check(bar.get("IsExpanded"), "Idle reminder stays visible while the player is absent")
	await click_control(game.get_node("ChipStack/ClickButton"))
	check(game.get("State") != 0 and not bar.get("IsIdleReminderActive"), "An actual bet releases the idle reminder")
	await create_timer(.7).timeout
	check(not bar.get("IsExpanded"), "Effective gameplay restores natural toolbar retraction")
	advance_idle(20.1)
	check(bar.get("IsIdleReminderActive"), "Another idle period reminds again before using any toolbar function")
	await click_host_control(host.get_node("InfoPanel/Panel/RootVBox/PayoutPanel/PaytableBtn"))
	var help = game.get_node("PokerHandShowcase")
	check(help.get("IsOverlayVisible") and help.get_node("Content/Scroll/Pages/Rows").visible, "Actual paytable button opens hand ranks")
	check(bar.get_node("HotZone").size.y == 160, "Paytable help does not count as a toolbar action")
	advance_idle(60)
	check(not bar.get("IsIdleReminderActive") and not bar.get("IsExpanded"), "Reading help suspends idle discovery")
	await click_control(help.get_node("Header/CloseButton"))
	await move_pointer(Vector2(1100, 950))
	advance_idle(19)
	check(not bar.get("IsIdleReminderActive"), "Closing paytable help restarts a full idle interval")
	advance_idle(1.1)
	check(bar.get("IsIdleReminderActive"), "Paytable help preserves future toolbar reminders")
	var settings = host.get_node("SettingsPanel")
	check(settings.get_node_or_null("Panel/RootVBox/Scroll/ContentVBox/SettingsContent/PokerGuideOverlayRow") == null, "Retired beginner guide setting is absent")
	settings.call("Open")
	await create_timer(.3).timeout
	check(bar.get("IsIdleReminderActive") and bar.get("IsExpanded"), "Opening the system panel preserves an active toolbar reminder")
	await click_host_control(settings.get_node("Panel/RootVBox/TitleRow/SettingsTab"))
	var content = settings.get_node("Panel/RootVBox/Scroll/ContentVBox/SettingsContent")
	check(content.is_visible_in_tree(), "Actual system-panel tab click opens settings")
	check(bar.get("IsIdleReminderActive"), "System-panel interaction does not release the toolbar reminder")
	bar.call("NotifyEffectiveInteraction")
	advance_idle(8)
	settings.call("CloseImmediate")
	await process_frame
	await process_frame
	advance_idle(5)
	settings.call("Open")
	await create_timer(.3).timeout
	await click_host_control(settings.get_node("Panel/RootVBox/TitleRow/WardrobeTab"))
	await click_host_control(settings.get_node("Panel/RootVBox/TitleRow/SettingsTab"))
	var scroll = settings.get_node("Panel/RootVBox/Scroll")
	var before_scroll = scroll.scroll_vertical
	var point = viewport_container.get_global_transform_with_canvas().affine_inverse() * content.get_node("GeneralLabel").get_global_rect().get_center()
	await move_pointer(point)
	await mouse_button(point, MOUSE_BUTTON_WHEEL_DOWN, true)
	await mouse_button(point, MOUSE_BUTTON_WHEEL_DOWN, false)
	check(scroll.scroll_vertical > before_scroll, "Actual wheel input scrolls the system panel")
	advance_idle(4)
	check(not bar.get("IsIdleReminderActive"), "System-panel activity does not trigger an early reminder")
	advance_idle(3.1)
	check(bar.get("IsIdleReminderActive") and bar.get("IsExpanded"), "Opening, closing, tab clicks and scrolling do not reset the accumulated 20 seconds")
	await click_host_control(settings.get_node("Panel/RootVBox/TitleRow/WardrobeTab"))
	check(bar.get("IsIdleReminderActive"), "Panel interaction also preserves an already expanded reminder")
	settings.call("CloseImmediate")
	await process_frame
	await process_frame
	check(bar.get("IsIdleReminderActive"), "Closing the panel does not clear the reminder")
	settings.call("Open")
	await process_frame
	bar.call("NotifyEffectiveInteraction")
	advance_idle(20.1)
	check(settings.get("IsOpen") and bar.get("IsIdleReminderActive"), "Idle discovery still works with the system panel open for the entire interval")
	settings.call("CloseImmediate")

func release_at_pointer():
	var local = Vector2(DisplayServer.mouse_get_position() - DisplayServer.window_get_position())
	var point = viewport_container.get_global_transform_with_canvas().affine_inverse() * local
	await mouse_button(point, MOUSE_BUTTON_LEFT, false)

func check_window_drag(point, description):
	await move_pointer(point)
	var before = DisplayServer.window_get_position()
	var state_before = game.get("State")
	await mouse_button(point, MOUSE_BUTTON_LEFT, true)
	await create_timer(.15).timeout
	# First motion arms the drag; the second must actually move the host.
	await move_pointer(point + Vector2(40, 20))
	await move_pointer(point + Vector2(90, 40))
	check(DisplayServer.window_get_position() != before, description + " moves host window")
	check(welcome.get("IsOverlayVisible") and not bar.get("IsExpanded"), description + " keeps welcome visible and toolbar collapsed")
	await release_at_pointer()
	var released_position = DisplayServer.window_get_position()
	await move_pointer(Vector2(1000, 1000))
	check(DisplayServer.window_get_position() == released_position, description + " releases without sticking")
	check(game.get("State") == state_before, description + " does not click through to poker")

func check_welcome_quickbar_action(action):
	var counts = {"welcome": 0, "toolbar": 0}
	game.connect("PokerWelcomeConfirmed", func(): counts.welcome += 1)
	game.connect("PokerQuickBarFunctionActivated", func(): counts.toolbar += 1)
	await move_pointer(Vector2(200, 40))
	await create_timer(.25).timeout
	check(welcome.get("IsOverlayVisible") and bar.get("IsExpanded"), action + ": hover reveals the bar over welcome")
	check(counts.welcome == 0 and counts.toolbar == 0, action + ": hover alone records neither acknowledgement nor use")
	var window_before = DisplayServer.window_get_position()
	var state_before = game.get("State")
	var button_point = bar.get_node("Clip/Bar/Buttons/" + action).get_global_rect().get_center()
	await move_pointer(button_point)
	await mouse_button(button_point, MOUSE_BUTTON_LEFT, true)
	check(DisplayServer.window_get_position() == window_before, action + ": pressing the toolbar button does not start a window drag")
	await mouse_button(button_point, MOUSE_BUTTON_LEFT, false)
	check(counts.welcome == 1 and counts.toolbar == 1, action + ": one click acknowledges welcome and records toolbar use once")
	check(not welcome.get("IsOverlayVisible") and not bar.get("IsExpanded"), action + ": direct use closes welcome without a five-second reveal")
	check(bar.get_node("HotZone").size.y == bar.get_node("Clip/Bar").size.y, action + ": direct use shrinks discovery region")
	check(game.get("State") == state_before, action + ": click does not play through the modal")
	if action != "DesktopPet":
		check(DisplayServer.window_get_position() == window_before, action + ": release does not drag the window")
	if action == "Help":
		var help = game.get_node("PokerHandShowcase")
		check(help.get("IsOverlayVisible") and help.get_node("Content/Scroll/Pages/Basics").visible, "Direct welcome help opens the basics page")
		await click_control(help.get_node("Header/CloseButton"))
	elif action == "Tutorial":
		var guide = game.get_node("TutorialManager")
		check(guide.get("IsOverlayVisible"), "Direct welcome controls opens the manual guide")
		await create_timer(2.1).timeout
		await mouse_button(Vector2(900, 900), MOUSE_BUTTON_LEFT, true)
		await mouse_button(Vector2(900, 900), MOUSE_BUTTON_LEFT, false)
		check(not guide.get("IsOverlayVisible"), "Direct welcome controls guide can close normally")
	else:
		check(host.get("CurrentMode") == 0, "Direct welcome desktop action returns to desktop mode")
		# Mode switching deliberately resizes/repositions the host before showing the pet.
		await create_timer(.5).timeout
		var desktop_position = DisplayServer.window_get_position()
		await move_pointer(Vector2(1100, 950))
		check(DisplayServer.window_get_position() == desktop_position, "Desktop mode handoff leaves no sticky window drag")
	check(not welcome.get("IsOverlayVisible"), action + ": dismissing the target does not restore welcome")
	if host.get("CurrentMode") != 0:
		host.call("SwitchToBossKey")
		await create_timer(.2).timeout
	host.call("SwitchToPlay")
	await create_timer(.4).timeout
	await move_pointer(Vector2(1100, 950))
	await create_timer(.6).timeout
	advance_idle(20.1)
	check(not welcome.get("IsOverlayVisible") and not bar.get("IsIdleReminderActive") and not bar.get("IsExpanded"), action + ": returning to poker remembers both records without lingering reveal")
	var account = ConfigFile.new()
	account.load("user://accounts/demo-debug/dev-steam_mock/account_state.cfg")
	check(not account.has_section_key("tutorial_progress", "poker_quick_bar_used") and not account.has_section_key("tutorial_progress", "poker_welcome_acknowledged"), action + ": Steam Mock keeps both records in memory")

func _initialize():
	call_deferred("run")

func run():
	var sandbox = "LuckyDogRise/Tests/PokerWelcomeIntegration-" + str(Time.get_unix_time_from_system()).replace(".", "-")
	ProjectSettings.set_setting("application/config/use_custom_user_dir", true)
	ProjectSettings.set_setting("application/config/custom_user_dir_name", sandbox)
	if not ProjectSettings.globalize_path("user://").replace("\\", "/").ends_with(sandbox + "/"):
		push_error("User directory was not isolated; main game not started.")
		quit(1)
		return
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("user://"))
	print("[WelcomeIntegration] user:// = ", ProjectSettings.globalize_path("user://"))
	var settings = ConfigFile.new()
	# The retired opt-out must not suppress discovery of the new toolbar.
	settings.set_value("system", "poker_guide_overlay_enabled", false)
	settings.set_value("system", "right_click_quick_mode_switch", true)
	settings.save("user://settings.cfg")
	var guard = root.get_node_or_null("SingleInstanceGuard")
	if guard:
		root.remove_child(guard)
		guard.free()
	host = load("res://Scenes/App/ModeManager.tscn").instantiate()
	root.add_child(host)
	current_scene = host
	await process_frame
	host.get_node("DeveloperLauncher").emit_signal("LaunchRequested", 1, 0, 1)
	await create_timer(2).timeout
	check(host.get("CurrentMode") == 0, "Startup stays in desktop mode")
	host.call("SwitchToPlay")
	await create_timer(.8).timeout
	viewport_container = host.get_node("PlayRoot/SubViewportContainer")
	game = viewport_container.get_node("SubViewport/Main")
	bar = game.get_node("PokerQuickBar")
	welcome = game.get_node("PokerWelcomeDialog")
	var guide = game.get_node("TutorialManager")
	await move_pointer(Vector2(1100, 1000))
	await create_timer(.6).timeout
	check(welcome.get("IsOverlayVisible") and not bar.get("IsExpanded"), "First entry shows welcome without revealing the toolbar")
	check(not guide.get("IsOverlayVisible"), "Automatic new-player guide does not stack over welcome")
	if "--debug-replay-settings" in OS.get_cmdline_user_args():
		await check_debug_replay_settings()
		print("[WelcomeIntegration] failures=", failures)
		quit(0 if failures == 0 else 1)
		return
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--welcome-action="):
			var action = argument.trim_prefix("--welcome-action=")
			check(action in ["Help", "Tutorial", "DesktopPet"], "Valid welcome action case")
			if failures == 0:
				await check_welcome_quickbar_action(action)
			print("[WelcomeIntegration] failures=", failures)
			quit(0 if failures == 0 else 1)
			return
	advance_idle(60)
	check(not bar.get("IsIdleReminderActive"), "Welcome suspends the idle reminder")
	await move_pointer(Vector2(1100, 1000))
	await create_timer(.8).timeout
	check(not bar.get("IsExpanded"), "Toolbar stays collapsed while welcome is visible")
	await check_window_drag(Vector2(1000, 1000), "Blank overlay drag")
	settings.set_value("system", "prevent_accidental_drag", true)
	settings.save("user://settings.cfg")
	var title = welcome.get_node("InputShield/Center/Panel/Margin/Content/Title")
	await check_window_drag(title.get_global_rect().get_center(), "Dialog text drag with accidental-drag protection")
	var confirm = welcome.get_node("InputShield/Center/Panel/Margin/Content/Confirm")
	var button_point = confirm.get_global_rect().get_center()
	await move_pointer(button_point)
	var original_position = DisplayServer.window_get_position()
	await mouse_button(button_point, MOUSE_BUTTON_LEFT, true)
	await create_timer(.15).timeout
	await move_pointer(Vector2(1000, 1000))
	await move_pointer(Vector2(950, 950))
	await release_at_pointer()
	check(DisplayServer.window_get_position() == original_position, "Gesture starting on confirm cannot drag host even outside button")
	check(welcome.get("IsOverlayVisible"), "Release outside confirm does not acknowledge")
	await move_pointer(Vector2(900, 950))
	check(DisplayServer.window_get_position() == original_position, "Canceled button gesture does not leave a stuck drag")
	await mouse_button(Vector2(900, 950), MOUSE_BUTTON_RIGHT, true)
	await mouse_button(Vector2(900, 950), MOUSE_BUTTON_RIGHT, false)
	check(host.get("CurrentMode") == 1 and welcome.get("IsOverlayVisible"), "Right click cannot switch mode through the welcome")
	game.call("TogglePokerHandShowcase")
	check(not game.get("IsPokerHandShowcaseVisible"), "Paytable help cannot cover the modal")
	await move_pointer(Vector2(200, 40))
	await create_timer(.25).timeout
	check(bar.get("IsExpanded") and welcome.get("IsOverlayVisible"), "Welcome allows hover discovery above its shield")
	var toolbar_drag_start = DisplayServer.window_get_position()
	await mouse_button(Vector2(200, 40), MOUSE_BUTTON_LEFT, true)
	await move_pointer(Vector2(900, 950))
	await release_at_pointer()
	await create_timer(.6).timeout
	check(DisplayServer.window_get_position() == toolbar_drag_start and not game.get("IsPokerHandShowcaseVisible") and welcome.get("IsOverlayVisible"), "Canceled toolbar gesture neither drags nor acknowledges welcome")
	check(not bar.get("IsExpanded"), "Welcome hover uses the ordinary half-second hide delay")
	host.call("SwitchToBossKey")
	await create_timer(.4).timeout
	check(not welcome.get("IsOverlayVisible"), "Leaving without acknowledgement hides welcome")
	host.call("SwitchToPlay")
	await create_timer(.4).timeout
	check(welcome.get("IsOverlayVisible") and not guide.get("IsOverlayVisible"), "Unacknowledged return shows welcome without guide")
	await check_window_drag(Vector2(1000, 1000), "Drag immediately before acknowledgement")
	await click_control(welcome.get_node("InputShield/Center/Panel/Margin/Content/Confirm"))
	check(not welcome.get("IsOverlayVisible") and bar.get("IsExpanded"), "Actual acknowledgement closes welcome and then reveals the toolbar")
	check(not guide.get("IsOverlayVisible"), "Acknowledgement never automatically opens the controls guide")
	await create_timer(.2).timeout
	check(bar.get("IsExpanded"), "Toolbar remains during initial close delay")
	await create_timer(.5).timeout
	check(bar.get("IsExpanded"), "Acknowledgement display lasts beyond the ordinary half-second delay")
	await create_timer(4.5).timeout
	check(not bar.get("IsExpanded"), "Toolbar retracts after five seconds outside following acknowledgement")
	host.call("SwitchToBossKey")
	await create_timer(.2).timeout
	host.call("SwitchToPlay")
	await create_timer(.2).timeout
	check(not guide.get("IsOverlayVisible") and not welcome.get("IsOverlayVisible"), "Unused toolbar does not cause the old entry guide to return")
	await check_idle_discovery(guide)
	await move_pointer(Vector2(200, 120))
	await create_timer(.25).timeout
	check(bar.get("IsExpanded"), "Welcome acknowledgement alone keeps the larger discovery region")
	await create_timer(.25).timeout
	await click_control(bar.get_node("Clip/Bar/Buttons/Help"))
	check(game.get("IsPokerHandShowcaseVisible"), "Help works normally after acknowledgement")
	check(game.get_node("PokerHandShowcase/Content/Scroll/Pages/Basics").visible, "Toolbar help still opens the basics page")
	check(bar.get_node("HotZone").size.y == bar.get_node("Clip/Bar").size.y, "First real toolbar action shrinks the input region to the bar body")
	await click_control(game.get_node("PokerHandShowcase/Header/CloseButton"))
	await move_pointer(Vector2(200, 120))
	await create_timer(.25).timeout
	check(not bar.get("IsExpanded"), "The former lower hot zone no longer reveals the toolbar")
	advance_idle(60)
	check(not bar.get("IsIdleReminderActive") and not bar.get("IsExpanded"), "Using a toolbar function permanently stops idle reminders")
	await move_pointer(Vector2(200, 40))
	await create_timer(.25).timeout
	await click_control(bar.get_node("Clip/Bar/Buttons/Tutorial"))
	check(guide.get("IsOverlayVisible"), "Controls guide remains available manually with the retired setting off")
	await create_timer(2.1).timeout
	await click_control(guide.get_node("OverlayInput"))
	check(not guide.get("IsOverlayVisible"), "Manual guide closes normally")
	await move_pointer(Vector2(1000, 900))
	await move_pointer(Vector2(200, 40))
	await create_timer(.25).timeout
	check(bar.get("IsExpanded"), "The compact hot zone still reveals the toolbar")
	await click_control(bar.get_node("Clip/Bar/Buttons/DesktopPet"))
	check(host.get("CurrentMode") == 0, "Desktop toolbar action still switches mode after shrinking")
	host.call("SwitchToBossKey")
	await create_timer(.4).timeout
	host.call("SwitchToPlay")
	await create_timer(.4).timeout
	check(not welcome.get("IsOverlayVisible"), "Confirmed welcome stays closed on same-run mode re-entry")
	await move_pointer(Vector2(1000, 120))
	await create_timer(.25).timeout
	check(not bar.get("IsExpanded"), "Compact hot zone survives mode re-entry")
	var before_lower_drag = DisplayServer.window_get_position()
	await mouse_button(Vector2(1000, 120), MOUSE_BUTTON_LEFT, true)
	await create_timer(.15).timeout
	await move_pointer(Vector2(1040, 120))
	await move_pointer(Vector2(1090, 120))
	check(DisplayServer.window_get_position() != before_lower_drag, "Removed hot-zone area allows normal window dragging")
	await release_at_pointer()
	advance_idle(60)
	check(not guide.get("IsOverlayVisible") and not bar.get("IsIdleReminderActive"), "Mode re-entry retains completed toolbar discovery without automatic controls")
	var account = ConfigFile.new()
	account.load("user://accounts/demo-debug/dev-steam_mock/account_state.cfg")
	check(not account.has_section_key("tutorial_progress", "poker_quick_bar_used"), "Steam Mock does not migrate or persist real discovery progress")
	print("[WelcomeIntegration] failures=", failures)
	quit(0 if failures == 0 else 1)
