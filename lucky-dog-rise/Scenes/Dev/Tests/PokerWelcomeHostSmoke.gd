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
	check(welcome.get("IsOverlayVisible") and bar.get("IsExpanded"), description + " keeps welcome and pinned toolbar visible")
	await release_at_pointer()
	var released_position = DisplayServer.window_get_position()
	await move_pointer(Vector2(1000, 1000))
	check(DisplayServer.window_get_position() == released_position, description + " releases without sticking")
	check(game.get("State") == state_before, description + " does not click through to poker")

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
	settings.set_value("system", "poker_guide_overlay_enabled", true)
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
	check(welcome.get("IsOverlayVisible") and bar.get("IsExpanded"), "First entry shows welcome and pins toolbar")
	check(not guide.get("IsOverlayVisible"), "Automatic new-player guide does not stack over welcome")
	await move_pointer(Vector2(1100, 1000))
	await create_timer(.8).timeout
	check(bar.get("IsExpanded"), "Real host pointer outside cannot collapse pinned bar")
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
	await click_control(bar.get_node("Clip/Bar/Buttons/Help"))
	check(not game.get("IsPokerHandShowcaseVisible") and welcome.get("IsOverlayVisible"), "Pinned toolbar is visible without click-through")
	host.call("SwitchToBossKey")
	await create_timer(.4).timeout
	check(not welcome.get("IsOverlayVisible"), "Leaving without acknowledgement hides welcome")
	host.call("SwitchToPlay")
	await create_timer(.4).timeout
	check(welcome.get("IsOverlayVisible") and not guide.get("IsOverlayVisible"), "Unacknowledged return shows welcome without guide")
	await check_window_drag(Vector2(1000, 1000), "Drag immediately before acknowledgement")
	await click_control(welcome.get_node("InputShield/Center/Panel/Margin/Content/Confirm"))
	check(not welcome.get("IsOverlayVisible"), "Actual SubViewport acknowledgement button closes welcome")
	check(not guide.get("IsOverlayVisible"), "Acknowledgement gives player time before automatic guide")
	await create_timer(.2).timeout
	check(bar.get("IsExpanded"), "Toolbar remains during initial close delay")
	await create_timer(.5).timeout
	check(not bar.get("IsExpanded"), "Toolbar retracts naturally after acknowledgement")
	await move_pointer(Vector2(200, 120))
	await create_timer(.25).timeout
	check(bar.get("IsExpanded"), "Welcome acknowledgement alone keeps the larger discovery region")
	await create_timer(.25).timeout
	await click_control(bar.get_node("Clip/Bar/Buttons/Help"))
	check(game.get("IsPokerHandShowcaseVisible"), "Help works normally after acknowledgement")
	check(bar.get_node("HotZone").size.y == bar.get_node("Clip/Bar").size.y, "First real toolbar action shrinks the input region to the bar body")
	await click_control(game.get_node("PokerHandShowcase/Header/CloseButton"))
	await move_pointer(Vector2(200, 120))
	await create_timer(.25).timeout
	check(not bar.get("IsExpanded"), "The former lower hot zone no longer reveals the toolbar")
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
	print("[WelcomeIntegration] failures=", failures)
	quit(0 if failures == 0 else 1)
