import java.util.ArrayList;
import java.util.List;

public class FleetManager {
    public static final int RECT_X = 200, RECT_Y = 150, RECT_W = 400, RECT_H = 250;

    public static List<DroneState> createMatrixFleet(int count, double centerX, double centerY) {
        List<DroneState> flotte = new ArrayList<>();
        int spacing = 40;

        for (int i = 0; i < count; i++) {
            double posX = centerX + (i - count / 2.0) * spacing; // alignement horizontal
            double posY = centerY; // tous sur la même ligne

            flotte.add(new DroneState(i + 1, posX, posY, 0));
        }

        return flotte;
    }
}
