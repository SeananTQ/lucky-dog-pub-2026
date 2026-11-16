shader_type canvas_item;

uniform vec2 card_size = vec2(120.0, 168.0);
// Controls use half-size; centered Sprite2D faces use (0, 0).
uniform vec2 card_center = vec2(60.0, 84.0);
uniform vec2 flip_direction = vec2(-1.0, 0.0);
uniform float flip_progress : hint_range(0.0, 1.0) = 0.0;

void vertex() {
	if (flip_progress > 0.0 && flip_progress < 1.0) {
		vec2 direction = normalize(flip_direction);
		vec2 center = card_center;
		vec2 point = VERTEX - center;
		float along = dot(point, direction);
		float angle = flip_progress * PI;
		float wave = sin(angle);
		// The approaching edge lifts first; after the face swap, the far edge lands.
		// abs(cos) keeps both printed faces readable instead of mirroring their text.
		float depth = -along * wave * (flip_progress < 0.5 ? 1.0 : -1.0);
		vec2 projected = point + direction * along * (abs(cos(angle)) - 1.0);
		float distance = length(card_size) * 2.8;
		float push = dot(abs(direction), card_size * 0.5) * 0.12 * wave;
		VERTEX = center + projected * distance / (distance - depth) + direction * push;
	}
}
