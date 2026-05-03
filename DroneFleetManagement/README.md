# Drone Fleet Mangement 

We have created 2 Java codes but they are not the same goal. 

## For the first : 

The number of drones is automatically calculated based on their altitude. The higher the drones fly, the larger their field of view on the ground. The program uses this information to determine the spacing between drones as well as the total number required to cover the entire search area.\
Once created, the drones are positioned with regular spacing. They then perform back-and-forth movements following a grid-based trajectory, with horizontal shifts after each pass. A start delay is applied between drones to prevent overlap and ensure coordinated movement.\
At each reset of the program, a target is randomly placed within the search area. During the simulation, the program continuously calculates the distance between each drone and the target. As soon as a drone enters its detection range, the target is considered found.\
The program then displays which drone detected the target, along with its coordinates. It also records the distance traveled by each drone, the estimated total mission time, and the total number of drones used.
<br>

#### CONCLUSION:
This program allows us to simulate and analyze the efficiency of a drone search strategy. It shows how parameters such as altitude and number of drones influence the coverage, coordination, and detection performance. Overall, the grid-based approach combined with proper spacing provides an effective and structured way to explore a search area.



## For the second : model obstacle avoidance
On the interface for our java code :  
→ we can change the number of drones  
→ the way drone flights, the drones can fly in circles or in grid. 
<br><br> 
<br><br> 
Our obstacles are represented by circles and rectangles. 
We have inclued condition : 
- If it's a circle, the drones must fly around it. 
- If it’s a rectangle, the drone must be able to climb to a higher altitude to avoid the obstacle. 
Also, when the drone climbs to a higher altitude, it appears red in the simulation. 
<br>

#### CONCLUSION :
We realised a lot of simulation and we conclued that the number of drones affect the round trips. 
For example, one of them, we use 10 drones and the other for thirty drones. 
When there are thirty drones, each drone will make fewer flights.
This programm, we permit to know the number of round trips for each drone depending on the number of drones. 

In addition, when we use a grid and circle. We have concluded that the simplest and most effective method is the one using a grid. The method using a circle can also be used, but there is a greater of overlap.
We also have identifed that when a drone arrive facing the circle, the programme consider that it's a rectangle, and create an error of simulation. 

