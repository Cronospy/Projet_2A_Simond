import java.awt.Point;
import java.util.ArrayList;
import java.util.List;

public class PathGenerator {
    public static List<Point> generateFullRectPath(int droneId, int totalDrones) {
        List<Point> path = new ArrayList<>();
        double stripW = (double) FleetManager.RECT_W / totalDrones;
        double xStart = FleetManager.RECT_X + (droneId - 1) * stripW;
        double xEnd = xStart + stripW;
        for (double x = xStart + 5; x < xEnd; x += 10) {
            path.add(new Point((int) x, FleetManager.RECT_Y + FleetManager.RECT_H));
            path.add(new Point((int) x, FleetManager.RECT_Y));
        }
        return path;
    }

    public static List<Point> generateCircularPath(int droneId, int totalDrones) {
        List<Point> path = new ArrayList<>();
        int cx = 400, cy = 275;
        double maxR = 230.0;
        double rStep = maxR / totalDrones;
        double r0 = maxR - droneId * rStep;

        for (double r = r0; r <= r0 + rStep; r += 8) {
            for (int a = 180; a <= 540; a += 15) {
                double rad = Math.toRadians(a);
                double px = cx + r * Math.cos(rad);
                double py = cy + r * Math.sin(rad);

                // clipping rectangle
                if (px < FleetManager.RECT_X) px = FleetManager.RECT_X;
                if (px > FleetManager.RECT_X + FleetManager.RECT_W) px = FleetManager.RECT_X + FleetManager.RECT_W;
                if (py < FleetManager.RECT_Y) py = FleetManager.RECT_Y;
                if (py > FleetManager.RECT_Y + FleetManager.RECT_H) py = FleetManager.RECT_Y + FleetManager.RECT_H;

                path.add(new Point((int) px, (int) py));
            }
            }


            return path;
        }
    }
