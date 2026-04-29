import java.awt.geom.Point2D;
import java.util.LinkedList;

public class DroneState {
    public int id;
    public double x, y, z;
    public double vx, vy;
    public double x_initiale, y_initiale;
    
    // Historique pour détection du Cas Critique (Surplace)
    public LinkedList<Point2D.Double> historiquePositions = new LinkedList<>();
    public boolean enUrgenceAltitude = false;

    public DroneState(int id, double x, double y, double z) {
        this.id = id;
        this.x = x; this.y = y; this.z = z;
        this.x_initiale = x; 
        this.y_initiale = y;
    }
}